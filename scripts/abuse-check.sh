#!/usr/bin/env bash
# Load and abuse sanity checks against a running instance (docs/PLAN.md Phase 6), e.g.
#   scripts/abuse-check.sh http://localhost:8080
# It uses up the sign-in rate limit of the calling IP address for a minute, so do not run it in CI next to other
# tests. Checks: request body limit, sign-in rate limit with Retry-After, anonymous uploads, and a short burst of
# page requests (no errors, latency printed).
set -uo pipefail

BASE_URL="${1:?usage: abuse-check.sh <base-url>}"
REQUESTS="${REQUESTS:-300}"
CONCURRENCY="${CONCURRENCY:-25}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
# Relative file names below: curl on Windows (Git Bash) cannot open /tmp paths inside --form values.
cd "$work" || exit 1
failures=0
pass() { echo "  ok    $1"; }
fail() { echo "  FAIL  $1"; failures=$((failures + 1)); }

# A session with the antiforgery token, as the Angular app has it.
jar="cookies"
curl --silent --cookie-jar "$jar" --output /dev/null "$BASE_URL/api/auth/me"
xsrf="$(awk '$6 == "XSRF-TOKEN" { print $7 }' "$jar")"
post() { # path, body file, extra curl args...
  local path="$1" body="$2"; shift 2
  curl --silent --cookie "$jar" --header "X-XSRF-TOKEN: $xsrf" --header "Content-Type: application/json" \
    --data-binary "@$body" --output /dev/null --write-out '%{http_code}' "$@" "$BASE_URL$path"
}

echo "Request body limit (1 MB for JSON)"
python3 -c "import json; print(json.dumps({'email': 'a@example.test', 'password': 'x' * 2_000_000}))" > big.json 2>/dev/null \
  || printf '{"email":"a@example.test","password":"%s"}' "$(head -c 2000000 /dev/zero | tr '\0' x)" > big.json
status="$(post /api/auth/login big.json)"
[ "$status" = 413 ] && pass "2 MB sign-in request -> 413" || fail "2 MB sign-in request -> $status (expected 413)"

echo "Anonymous uploads are refused before the file is processed"
head -c 12000000 /dev/urandom > big.bin
status="$(curl --silent --cookie "$jar" --header "X-XSRF-TOKEN: $xsrf" --form "file=@big.bin;type=image/jpeg" \
  --output /dev/null --write-out '%{http_code}' "$BASE_URL/api/chat/attachments")"
case "$status" in 401|413) pass "12 MB anonymous upload -> $status" ;; *) fail "12 MB anonymous upload -> $status (expected 401 or 413)" ;; esac

echo "Sign-in rate limit (10 per minute per IP address)"
printf '{"email":"nobody@example.test","password":"wrong password 123"}' > login.json
limited=""
for attempt in $(seq 1 12); do
  status="$(post /api/auth/login login.json --dump-header headers)"
  if [ "$status" = 429 ]; then limited="$attempt"; break; fi
done
if [ -n "$limited" ] && grep -qi '^retry-after:' headers; then
  pass "attempt $limited -> 429 with Retry-After"
else
  fail "no 429 with Retry-After within 12 sign-in attempts"
fi

echo "Burst of $REQUESTS page requests, $CONCURRENCY at a time"
seq 1 "$REQUESTS" | xargs -P "$CONCURRENCY" -I{} \
  curl --silent --output /dev/null --write-out '%{http_code} %{time_total}\n' "$BASE_URL/en/" > burst
errors="$(awk '$1 != 200' burst | wc -l | tr -d ' ')"
sort -k2 -n burst | awk '{ t[NR] = $2 } END { printf "  info  median %.3fs, p95 %.3fs, max %.3fs\n", t[int(NR*0.5)], t[int(NR*0.95)], t[NR] }'
[ "$errors" = 0 ] && pass "all $REQUESTS answered 200" || fail "$errors of $REQUESTS requests did not answer 200"

if [ "$failures" -gt 0 ]; then echo "Abuse check FAILED: $failures check(s)."; exit 1; fi
echo "Abuse check passed."
