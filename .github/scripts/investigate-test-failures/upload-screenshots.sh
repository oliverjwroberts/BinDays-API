#!/bin/bash
# Upload investigation screenshots to R2 and write screenshot-urls.json.
#
# Looks for *-website.png files in .agent/playwright/out/, uploads each to
# R2, and writes a JSON array to screenshot-urls.json:
#   [{"councilName": "BarnetCouncil", "url": "https://..."}, ...]
#
# Required environment variables:
#   AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_ENDPOINT_URL
#   CLOUDFLARE_R2_BUCKET_NAME
#   CLOUDFLARE_R2_CUSTOM_DOMAIN (optional)
#   CLOUDFLARE_R2_ACCOUNT_ID (required if no custom domain)
#   GITHUB_RUN_ID (set automatically by GitHub Actions)

set -e

SCREENSHOT_DIR=".agent/playwright/out"
OUTPUT_FILE="screenshot-urls.json"
BUCKET="$CLOUDFLARE_R2_BUCKET_NAME"

if ! ls "$SCREENSHOT_DIR"/*-website.png 2>/dev/null | grep -q .; then
  echo "No website screenshots found in $SCREENSHOT_DIR"
  echo "[]" > "$OUTPUT_FILE"
  exit 0
fi

OUTPUT="["
FIRST=true

for SCREENSHOT_FILE in "$SCREENSHOT_DIR"/*-website.png; do
  BASENAME=$(basename "$SCREENSHOT_FILE")
  COUNCIL_NAME="${BASENAME%-website.png}"
  R2_KEY="screenshots/investigations/${GITHUB_RUN_ID}/${BASENAME}"

  echo "Uploading $SCREENSHOT_FILE..."
  if aws s3 cp "$SCREENSHOT_FILE" "s3://${BUCKET}/${R2_KEY}" \
      --endpoint-url "$AWS_ENDPOINT_URL" \
      --acl public-read \
      --content-type image/png; then

    if [ -n "$CLOUDFLARE_R2_CUSTOM_DOMAIN" ]; then
      URL="https://${CLOUDFLARE_R2_CUSTOM_DOMAIN}/${BUCKET}/${R2_KEY}"
    else
      URL="https://${BUCKET}.${CLOUDFLARE_R2_ACCOUNT_ID}.r2.cloudflarestorage.com/${R2_KEY}"
    fi

    [ "$FIRST" = true ] && FIRST=false || OUTPUT="${OUTPUT},"
    OUTPUT="${OUTPUT}{\"councilName\":\"${COUNCIL_NAME}\",\"url\":\"${URL}\"}"
    echo "Uploaded -> $URL"
  else
    echo "Failed to upload $SCREENSHOT_FILE — skipping"
  fi
done

OUTPUT="${OUTPUT}]"
echo "$OUTPUT" > "$OUTPUT_FILE"
echo "Wrote $OUTPUT_FILE"
