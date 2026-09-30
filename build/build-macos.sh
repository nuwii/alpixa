#!/usr/bin/env bash
# Alpixa icin macOS kurulum dosyasi (artifacts/Alpixa.dmg) uretir.
#
# Imzali ve notarize edilmis paket icin su ortam degiskenleri gerekir:
#   APPLE_SIGNING_IDENTITY   ornek: "Developer ID Application: Firma Adi (TEAMID)"
#   APPLE_TEAM_ID            Apple Developer takim kimligi
#   APPLE_ID                 Apple Developer hesabinin e-posta adresi
#   APPLE_APP_PASSWORD       appleid.apple.com'dan alinan uygulamaya ozel sifre
#   APPLE_PROVISIONING_PROFILE (istege bagli) Developer ID provisioning profile adi veya UUID
#
# Sadece kendi bilgisayarinizda denemek icin imzasiz paket:
#   ./build/build-macos.sh --unsigned
#
# Gereksinimler: macOS, Xcode, .NET 10 SDK, MAUI workload (dotnet workload install maui).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts"
PROJECT="$ROOT/src/Alpixa.App/Alpixa.App.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${VERSION:-1.0.0}"
UNSIGNED=false
[[ "${1:-}" == "--unsigned" ]] && UNSIGNED=true

fail() { echo "HATA: $*" >&2; exit 1; }

command -v dotnet >/dev/null || fail ".NET SDK bulunamadi. https://dotnet.microsoft.com/download adresinden .NET 10 SDK kurun."
xcodebuild -version >/dev/null 2>&1 || fail "Xcode bulunamadi. App Store'dan Xcode kurun, sonra: sudo xcode-select -s /Applications/Xcode.app"

SIGN_ARGS=(-p:EnableCodeSigning=false)
if [[ "$UNSIGNED" == false ]]; then
  : "${APPLE_SIGNING_IDENTITY:?APPLE_SIGNING_IDENTITY tanimli degil (imzasiz paket icin --unsigned kullanin)}"
  : "${APPLE_TEAM_ID:?APPLE_TEAM_ID tanimli degil}"
  : "${APPLE_ID:?APPLE_ID tanimli degil}"
  : "${APPLE_APP_PASSWORD:?APPLE_APP_PASSWORD tanimli degil}"
  SIGN_ARGS=(-p:EnableCodeSigning=true -p:CodesignKey="$APPLE_SIGNING_IDENTITY" -p:UseHardenedRuntime=true)
  [[ -n "${APPLE_PROVISIONING_PROFILE:-}" ]] && SIGN_ARGS+=(-p:CodesignProvision="$APPLE_PROVISIONING_PROFILE")
fi

echo "==> Uygulama derleniyor ($CONFIGURATION, Apple Silicon + Intel)..."
rm -rf "$ARTIFACTS/mac"
mkdir -p "$ARTIFACTS/mac"
dotnet publish "$PROJECT" \
  -f net10.0-maccatalyst \
  -c "$CONFIGURATION" \
  -p:CreatePackage=false \
  -p:ApplicationDisplayVersion="$VERSION" \
  "${SIGN_ARGS[@]}"

# The universal (Apple Silicon + Intel) bundle sits directly under the framework folder;
# the per-architecture copies live in maccatalyst-arm64/ and maccatalyst-x64/.
APP="$ROOT/src/Alpixa.App/bin/$CONFIGURATION/net10.0-maccatalyst/Alpixa.app"
[[ -d "$APP" ]] || fail ".app paketi bulunamadi."
cp -R "$APP" "$ARTIFACTS/mac/Alpixa.app"

if [[ "$UNSIGNED" == true ]]; then
  # Apple Silicon refuses to run code with no signature at all; an ad-hoc signature is enough to open it.
  codesign --force --deep --sign - "$ARTIFACTS/mac/Alpixa.app"
fi

echo "==> DMG olusturuluyor..."
STAGE="$(mktemp -d)"
cp -R "$ARTIFACTS/mac/Alpixa.app" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
DMG="$ARTIFACTS/Alpixa.dmg"
rm -f "$DMG"
hdiutil create -volname "Alpixa" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"

if [[ "$UNSIGNED" == false ]]; then
  echo "==> DMG imzalaniyor..."
  codesign --sign "$APPLE_SIGNING_IDENTITY" --timestamp "$DMG"
  echo "==> Apple notarization (birkac dakika surebilir)..."
  xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
  xcrun stapler staple "$DMG"
else
  echo "Not: Imzasiz paket. Baska bir Mac'te acilirken 'internetten indirildi / dogrulanamadi' uyarisi cikar."
fi

echo "==> Hazir: $DMG"
