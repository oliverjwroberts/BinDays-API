namespace BinDays.Api.Collectors.Utilities;

using BinDays.Api.Collectors.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Utility for solving Imperva/Incapsula bot protection cookie challenges without browser emulation.
///
/// Two challenge variants exist at Southampton:
///
///   Script challenge (212-byte response) — served to clients with a "semi-trusted" TLS fingerprint.
///     The page contains a &lt;script src="/_Incapsula_Resource?SWJIYLWA=..."&gt; tag.
///     The solver handles this via a three-step flow:
///       1. Detect the challenge; extract the script URL and session cookies.
///       2. Fetch the challenge script (returned as a client-side response).
///       3. Execute the script via Node.js subprocess using a Proxy sandbox, computing ___utmvc.
///       4. Retry the original URL with all cookies including ___utmvc.
///
///   Iframe challenge (850-byte response) — served to clients with a "known-bot" TLS fingerprint
///     (e.g. Dart/BoringSSL on Windows). Contains an iframe with SWUDNSAI=31 and edet=12.
///     This variant requires CAPTCHA-level solving and cannot be bypassed without JavaScript
///     execution in a trusted browser context. The solver falls back to a cookies-only bypass
///     attempt and throws if that also fails.
///
/// For simpler (older) deployments that inline the challenge JavaScript as a hex-encoded
/// "var Z = ..." block, the solver directly computes ___utmvc via known btoa() patterns.
/// </summary>
internal static partial class IncapsulaSolver
{
	/// <summary>
	/// The metadata key used to carry Incapsula cookies across request/response steps.
	/// </summary>
	internal const string MetadataKey = "incapsula_cookie";

	/// <summary>
	/// The metadata key used to store the challenge script URL for the script-fetch step.
	/// </summary>
	private const string _scriptUrlMetadataKey = "incapsula_script_url";

	/// <summary>
	/// The request ID used for the challenge-script fetch step.
	/// Collectors that use this solver must not use RequestId 50 for other purposes.
	/// </summary>
	internal const int ScriptFetchRequestId = 50;

	/// <summary>
	/// Inline Node.js runner passed via stdin to compute ___utmvc from the challenge script.
	/// Accepts a JSON object { scriptContent, cookiesJson, userAgent, url } and writes the
	/// ___utmvc value to stdout. Uses a Proxy sandbox to stub all browser APIs the script accesses.
	/// </summary>
	private const string _nodeJsRunner = """
		const vm=require('vm');
		const d=JSON.parse(require('fs').readFileSync(0,'utf8'));
		const jar=JSON.parse(d.cookiesJson);
		const UA=d.userAgent;
		function E(){return{style:{},innerHTML:'',src:'',setAttribute:()=>{},getAttribute:()=>null,appendChild:()=>{},addEventListener:()=>{},complete:true,tagName:'div'};}
		const doc={location:{href:d.url,reload:()=>{}},getElementById:()=>E(),querySelector:()=>E(),querySelectorAll:()=>[],createElement:(t)=>Object.assign(E(),{tagName:t}),createElementNS:()=>E(),head:{appendChild:()=>{},removeChild:()=>{}},body:{appendChild:()=>{},removeChild:()=>{},offsetWidth:1920},documentElement:{lang:'en-GB'},addEventListener:()=>{},removeEventListener:()=>{},characterSet:'UTF-8',referrer:'',title:'',visibilityState:'visible',hidden:false};
		Object.defineProperty(doc,'cookie',{get:()=>Object.entries(jar).map(([k,v])=>k+'='+v).join('; '),set:(v)=>{const[p]=v.split(';');const[k,...val]=p.split('=');jar[k.trim()]=val.join('=');}});
		function FXR(){this.open=()=>{};this.send=()=>{};this.setRequestHeader=()=>{};this.readyState=4;this.status=200;this.responseText='';this.getAllResponseHeaders=()=>'';}
		const sb=new Proxy({document:doc,navigator:{userAgent:UA,language:'en-GB',languages:['en-GB','en'],platform:'Win32',plugins:[],cookieEnabled:true,hardwareConcurrency:4,maxTouchPoints:0,onLine:true,mimeTypes:[],javaEnabled:()=>false,sendBeacon:()=>true,doNotTrack:null},screen:{width:1920,height:1080,colorDepth:24,pixelDepth:24,availWidth:1920,availHeight:1040,orientation:{type:'landscape-primary',angle:0}},location:doc.location,history:{length:1,pushState:()=>{},replaceState:()=>{}},performance:{now:()=>Date.now(),timing:{navigationStart:Date.now()-3000},navigation:{type:0,redirectCount:0}},XMLHttpRequest:FXR,Worker:function(){this.postMessage=()=>{};this.terminate=()=>{};this.addEventListener=()=>{};},Image:function(w,h){this.src='';this.complete=true;this.width=w||0;this.height=h||0;this.addEventListener=()=>{};},MutationObserver:function(){this.observe=()=>{};this.disconnect=()=>{};},btoa:(s)=>Buffer.from(s,'binary').toString('base64'),atob:(s)=>Buffer.from(s,'base64').toString('binary'),parseInt,parseFloat,isNaN,isFinite,encodeURIComponent,decodeURIComponent,encodeURI,decodeURI,JSON,Math,Date,Array,Object,String,RegExp,Error,Promise,Function,Symbol,Map,Set,setTimeout:(fn)=>{try{if(typeof fn==='function')fn();}catch(e){}},clearTimeout:()=>{},setInterval:()=>{},clearInterval:()=>{},requestAnimationFrame:(fn)=>{try{fn(Date.now());}catch(e){}},cancelAnimationFrame:()=>{},addEventListener:()=>{},removeEventListener:()=>{},dispatchEvent:()=>{},getComputedStyle:()=>({getPropertyValue:()=>'',width:'1920px'}),matchMedia:()=>({matches:false,addListener:()=>{},removeEventListener:()=>{}}),crypto:{getRandomValues:(a)=>{for(let i=0;i<a.length;i++)a[i]=Math.floor(Math.random()*256);return a;},subtle:{}},console:{log:()=>{},warn:()=>{},error:()=>{}},undefined},{get(t,p){if(p in t)return t[p];return new Proxy(function(){},{construct(t,a){return{};},apply(t,th,a){return undefined;},get(t,pp){if(pp==='prototype')return{};return undefined;}});}});
		sb.window=sb;sb.self=sb;sb.top=sb;sb.parent=sb;doc.defaultView=sb;
		try{vm.createContext(sb);new vm.Script(d.scriptContent,{timeout:8000}).runInContext(sb);}catch(e){}
		process.stdout.write(jar['___utmvc']||'');
		""";

	/// <summary>
	/// Regex to detect the Incapsula resource reference that appears in challenge pages.
	/// </summary>
	[GeneratedRegex(@"/_Incapsula_Resource")]
	private static partial Regex IncapsulaResourceRegex();

	/// <summary>
	/// Regex to detect and extract the script-challenge URL (SWJIYLWA variant).
	/// This compact form (under ~300 bytes with a &lt;script src&gt; tag) indicates a solvable challenge.
	/// </summary>
	[GeneratedRegex(@"<script\s+src=""(?<path>/_Incapsula_Resource\?SWJIYLWA=[^""]+)""")]
	private static partial Regex ScriptChallengeUrlRegex();

	/// <summary>
	/// Regex to extract the hex-encoded challenge script from the page HTML.
	/// </summary>
	[GeneratedRegex(@"var\s+Z\s*=\s*""(?<hex>[0-9a-fA-F]+)""")]
	private static partial Regex ChallengeHexRegex();

	/// <summary>
	/// Regex to extract visid_incap_* name/value pairs from a combined Set-Cookie header.
	/// </summary>
	[GeneratedRegex(@"(?<name>visid_incap_\d+)=(?<value>[^\s;,]+)")]
	private static partial Regex VisidIncapRegex();

	/// <summary>
	/// Regex to extract incap_ses_* name/value pairs from a combined Set-Cookie header.
	/// </summary>
	[GeneratedRegex(@"(?<name>incap_ses_[^\s=;,]+)=(?<value>[^\s;,]+)")]
	private static partial Regex IncapSesRegex();

	/// <summary>
	/// Regex to detect the btoa(visidCookie + navigator.userAgent) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*c\[1\]\s*\+\s*navigator\.userAgent\s*\)")]
	private static partial Regex BtoaVisidUserAgentRegex();

	/// <summary>
	/// Regex to detect the btoa(navigator.userAgent) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*navigator\.userAgent\s*\)")]
	private static partial Regex BtoaUserAgentRegex();

	/// <summary>
	/// Regex to detect the btoa(document.cookie) computation pattern.
	/// </summary>
	[GeneratedRegex(@"btoa\(\s*document\.cookie\s*\)")]
	private static partial Regex BtoaDocumentCookieRegex();

	/// <summary>
	/// Returns true if the response is any Imperva/Incapsula bot protection challenge page
	/// (either the script-challenge or the iframe-challenge variant).
	/// </summary>
	public static bool IsChallenge(ClientSideResponse response)
	{
		if (IncapsulaResourceRegex().IsMatch(response.Content))
		{
			return true;
		}

		if (response.Headers.TryGetValue("set-cookie", out var setCookieHeader) &&
			setCookieHeader.Contains("visid_incap_"))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true if the response is the script-challenge variant — a short page that contains
	/// a &lt;script src="/_Incapsula_Resource?SWJIYLWA=..."&gt; tag. This form is served to
	/// clients with a semi-trusted TLS fingerprint and can be solved via script execution.
	/// </summary>
	public static bool IsScriptChallenge(ClientSideResponse response)
	{
		return ScriptChallengeUrlRegex().IsMatch(response.Content);
	}

	/// <summary>
	/// Extracts the full challenge script URL from a script-challenge response, resolved against
	/// the given base URL. Returns null if no script URL is found.
	/// </summary>
	public static string? ExtractScriptUrl(ClientSideResponse response, string baseUrl)
	{
		var match = ScriptChallengeUrlRegex().Match(response.Content);
		if (!match.Success)
		{
			return null;
		}

		var path = match.Groups["path"].Value;
		var baseUri = new Uri(baseUrl);
		return new Uri(baseUri, path).ToString();
	}

	/// <summary>
	/// Builds a request to fetch the challenge script URL. The response (script content) should
	/// be passed back to <see cref="BuildBypassRequestFromScript"/> to compute ___utmvc.
	/// The challenge cookies and original URL are stored in metadata for the next step.
	/// </summary>
	public static ClientSideRequest BuildScriptFetchRequest(
		ClientSideResponse challengeResponse,
		string scriptUrl,
		string originalUrl)
	{
		var challengeCookies = ExtractChallengeCookies(challengeResponse);
		var cookieHeader = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
		var cookiesJson = JsonSerializer.Serialize(challengeCookies);

		return new ClientSideRequest
		{
			RequestId = ScriptFetchRequestId,
			Url = scriptUrl,
			Method = "GET",
			Headers = new()
			{
				{ "user-agent", Constants.UserAgent },
				{ "cookie", cookieHeader },
				{ "referer", originalUrl },
			},
			Options = new ClientSideOptions
			{
				Metadata =
				{
					{ MetadataKey, cookieHeader },
					{ _scriptUrlMetadataKey, cookiesJson },
				},
			},
		};
	}

	/// <summary>
	/// Builds the final bypass request after the challenge script has been fetched.
	/// Executes the script content via Node.js subprocess to compute ___utmvc, then
	/// constructs a retry of <paramref name="originalRequest"/> with the full cookie set.
	/// Falls back to a cookies-only bypass if Node.js is unavailable or execution fails.
	/// </summary>
	public static ClientSideRequest BuildBypassRequestFromScript(
		ClientSideRequest originalRequest,
		ClientSideResponse scriptResponse)
	{
		var cookiesJson = scriptResponse.Options.Metadata[_scriptUrlMetadataKey];
		var challengeCookies = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson)!;

		var scriptContent = scriptResponse.Content;
		var utmvc = TryExecuteScriptViaNodeJs(scriptContent, challengeCookies, originalRequest.Url);

		if (!string.IsNullOrEmpty(utmvc))
		{
			challengeCookies["___utmvc"] = utmvc;
		}

		var incapsulaCookieHeader = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));

		var headers = new Dictionary<string, string>(originalRequest.Headers)
		{
			["cookie"] = incapsulaCookieHeader,
		};

		return new ClientSideRequest
		{
			RequestId = originalRequest.RequestId,
			Url = originalRequest.Url,
			Method = originalRequest.Method,
			Headers = headers,
			Body = originalRequest.Body,
			Options = new ClientSideOptions
			{
				FollowRedirects = originalRequest.Options.FollowRedirects,
				Metadata = { [MetadataKey] = incapsulaCookieHeader },
			},
		};
	}

	/// <summary>
	/// Builds a bypass request for the given original request using cookies extracted from the
	/// Incapsula challenge response. Used for the cookies-only bypass path (no script execution)
	/// and for older inline-script challenge deployments. The solved Incapsula cookies are also
	/// stored in Options.Metadata under <see cref="MetadataKey"/> for use in subsequent requests.
	/// </summary>
	public static ClientSideRequest BuildBypassRequest(
		ClientSideRequest originalRequest,
		ClientSideResponse challengeResponse)
	{
		var challengeCookies = ExtractChallengeCookies(challengeResponse);
		var utmvcValue = ComputeUtmvcValue(challengeResponse.Content, challengeCookies);

		if (!string.IsNullOrEmpty(utmvcValue))
		{
			challengeCookies["___utmvc"] = utmvcValue;
		}

		var incapsulaCookieHeader = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));

		var headers = new Dictionary<string, string>(originalRequest.Headers);

		if (headers.TryGetValue("cookie", out var existingCookie) && !string.IsNullOrEmpty(existingCookie))
		{
			headers["cookie"] = $"{incapsulaCookieHeader}; {existingCookie}";
		}
		else
		{
			headers["cookie"] = incapsulaCookieHeader;
		}

		return new ClientSideRequest
		{
			RequestId = originalRequest.RequestId,
			Url = originalRequest.Url,
			Method = originalRequest.Method,
			Headers = headers,
			Body = originalRequest.Body,
			Options = new ClientSideOptions
			{
				FollowRedirects = originalRequest.Options.FollowRedirects,
				Metadata = { [MetadataKey] = incapsulaCookieHeader },
			},
		};
	}

	/// <summary>
	/// Returns the Incapsula cookie string stored in the response metadata, or null if no bypass
	/// was performed for this response's request.
	/// </summary>
	public static string? GetStoredCookie(ClientSideResponse response)
	{
		return response.Options.Metadata.GetValueOrDefault(MetadataKey);
	}

	/// <summary>
	/// Executes the Incapsula challenge script in a Node.js subprocess using a Proxy sandbox that
	/// stubs all browser APIs. Returns the computed ___utmvc cookie value, or an empty string if
	/// Node.js is not available, execution times out, or the script does not set ___utmvc.
	/// The runner script is written to a temp file so that the Node.js process can read the
	/// JSON input data from stdin separately.
	/// </summary>
	private static string TryExecuteScriptViaNodeJs(
		string scriptContent,
		Dictionary<string, string> challengeCookies,
		string pageUrl)
	{
		var input = JsonSerializer.Serialize(new
		{
			scriptContent,
			cookiesJson = JsonSerializer.Serialize(challengeCookies),
			userAgent = Constants.UserAgent,
			url = pageUrl,
		});

		var runnerPath = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"bindays_incapsula_runner_{Guid.NewGuid():N}.js"
		);

		try
		{
			System.IO.File.WriteAllText(runnerPath, _nodeJsRunner);

			using var process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "node",
					Arguments = $"\"{runnerPath}\"",
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = false,
					UseShellExecute = false,
					CreateNoWindow = true,
				},
			};

			process.Start();
			process.StandardInput.WriteLine(input);
			process.StandardInput.Close();

			if (process.WaitForExit(10_000))
			{
				return process.StandardOutput.ReadToEnd().Trim();
			}

			try
			{
				process.Kill();
			}
			catch
			{
				// Ignore kill failure
			}

			return string.Empty;
		}
		catch
		{
			return string.Empty;
		}
		finally
		{
			try
			{
				if (System.IO.File.Exists(runnerPath))
				{
					System.IO.File.Delete(runnerPath);
				}
			}
			catch
			{
				// Ignore cleanup failure
			}
		}
	}

	/// <summary>
	/// Extracts visid_incap_* and incap_ses_* cookies from the challenge response's Set-Cookie header.
	/// </summary>
	private static Dictionary<string, string> ExtractChallengeCookies(ClientSideResponse response)
	{
		var cookies = new Dictionary<string, string>();

		if (!response.Headers.TryGetValue("set-cookie", out var setCookieHeader))
		{
			return cookies;
		}

		foreach (Match match in VisidIncapRegex().Matches(setCookieHeader)!)
		{
			cookies[match.Groups["name"].Value] = match.Groups["value"].Value;
		}

		foreach (Match match in IncapSesRegex().Matches(setCookieHeader)!)
		{
			cookies[match.Groups["name"].Value] = match.Groups["value"].Value;
		}

		return cookies;
	}

	/// <summary>
	/// Attempts to compute the ___utmvc cookie value by decoding and analysing the Incapsula
	/// challenge script. Returns an empty string if the computation pattern is not recognised.
	/// Used for older inline-script ("var Z = ...") deployments only.
	///
	/// Known patterns matched (in priority order):
	///   btoa(visid_value + navigator.userAgent)  — most common
	///   btoa(navigator.userAgent)                — simpler variant
	///   btoa(document.cookie)                    — encodes all cookies
	/// </summary>
	private static string ComputeUtmvcValue(
		string challengeHtml,
		Dictionary<string, string> challengeCookies)
	{
		var hexMatch = ChallengeHexRegex().Match(challengeHtml);
		if (!hexMatch.Success)
		{
			return string.Empty;
		}

		var decoded = DecodeHexToString(hexMatch.Groups["hex"].Value);

		var visidValue = challengeCookies
			.FirstOrDefault(kvp => kvp.Key.StartsWith("visid_incap_")).Value ?? string.Empty;

		if (BtoaVisidUserAgentRegex().IsMatch(decoded))
		{
			return Base64Encode(visidValue + Constants.UserAgent);
		}

		if (BtoaUserAgentRegex().IsMatch(decoded))
		{
			return Base64Encode(Constants.UserAgent);
		}

		if (BtoaDocumentCookieRegex().IsMatch(decoded))
		{
			var cookieString = string.Join("; ", challengeCookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
			return Base64Encode(cookieString);
		}

		return string.Empty;
	}

	/// <summary>
	/// Decodes a hex string (two hex digits per byte) to a UTF-8 string.
	/// Returns an empty string if the hex string has an odd length.
	/// </summary>
	private static string DecodeHexToString(string hex)
	{
		if (hex.Length % 2 != 0)
		{
			return string.Empty;
		}

		var bytes = new byte[hex.Length / 2];
		for (var i = 0; i < bytes.Length; i++)
		{
			bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}

		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// Base64-encodes a string using Latin-1 (ISO-8859-1) byte representation, exactly matching
	/// JavaScript's btoa() behaviour, which treats each character as a single byte (0–255).
	/// </summary>
	private static string Base64Encode(string value)
	{
		return Convert.ToBase64String(Encoding.Latin1.GetBytes(value));
	}
}
