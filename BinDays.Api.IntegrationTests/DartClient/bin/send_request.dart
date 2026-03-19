import 'dart:convert';
import 'dart:io';
import 'package:dio/dio.dart' as dio;

import 'package:bindays_client/client.dart';
import 'package:bindays_client/models/client_side_request.dart';

Future<void> main() async {
  try {
    final input = await stdin.transform(utf8.decoder).join();
    final json = jsonDecode(input) as Map<String, dynamic>;
    final request = ClientSideRequest.fromJson(json);

    // Dummy base URL — we only use sendClientSideRequest, not the API methods.
    final client = Client(Uri.parse('http://localhost'));

    final enableLogging = Platform.environment['BINDAYS_ENABLE_HTTP_LOGGING']?.toLowerCase() == 'true';
    if (enableLogging) {
      client.httpClient.interceptors.add(dio.LogInterceptor(
        requestBody: true,
        responseBody: true,
        logPrint: (message) => stderr.writeln(message),
      ));
    }

    final response = await client.sendClientSideRequest(request, validateStatus: false);

    stdout.write(jsonEncode(response.toJson()));
    client.httpClient.close();
  } on dio.DioException catch (e) {
    stderr.write('DioException: ${e.message}');
    exit(1);
  } catch (e) {
    stderr.write('Error: $e');
    exit(1);
  }
}
