#!/bin/bash
# Build script for macOS app bundle and DMG installer

set -e

# Configuration
APP_NAME="TerminalHost"
BUNDLE_ID="com.terminalhost.app"
VERSION="1.0.0"
PROJECT_PATH="src/TerminalHost/TerminalHost"
OUTPUT_DIR="publish"

# Parse arguments
CREATE_DMG=false
SKIP_BUILD=false
for arg in "$@"; do
    case $arg in
        --dmg)
            CREATE_DMG=true
            shift
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --dmg         Create DMG installer after building"
            echo "  --skip-build  Skip .NET build, just create app bundle/DMG"
            echo "  --help, -h    Show this help"
            echo ""
            echo "Environment variables:"
            echo "  CODESIGN_IDENTITY  Apple Developer identity for code signing"
            exit 0
            ;;
    esac
done

# Determine architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RUNTIME="osx-arm64"
else
    RUNTIME="osx-x64"
fi

echo "Building TerminalHost for macOS ($ARCH)..."

if [ "$SKIP_BUILD" = false ]; then
    # Clean previous build
    rm -rf "$OUTPUT_DIR"

    # Build the project
    echo "Publishing .NET project..."
    dotnet publish "$PROJECT_PATH" \
        -c Release \
        -r "$RUNTIME" \
        --self-contained true \
        -o "$OUTPUT_DIR/$RUNTIME"
fi

# Create app bundle structure
echo "Creating app bundle structure..."
APP_BUNDLE="$OUTPUT_DIR/${APP_NAME}.app"
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy executable and dependencies
echo "Copying files to app bundle..."
cp -R "$OUTPUT_DIR/$RUNTIME/"* "$APP_BUNDLE/Contents/MacOS/"

# Copy Info.plist
if [ -f "$PROJECT_PATH/Info.plist" ]; then
    cp "$PROJECT_PATH/Info.plist" "$APP_BUNDLE/Contents/"
else
    # Create a basic Info.plist if not exists
    cat > "$APP_BUNDLE/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleExecutable</key>
    <string>host</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>app.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSRequiresAquaSystemAppearance</key>
    <false/>
</dict>
</plist>
EOF
fi

# Copy icon if exists
if [ -f "$PROJECT_PATH/Resources/app.icns" ]; then
    cp "$PROJECT_PATH/Resources/app.icns" "$APP_BUNDLE/Contents/Resources/"
    echo "App icon copied."
else
    echo "Warning: No app.icns found at $PROJECT_PATH/Resources/app.icns"
fi

# Make executable
chmod +x "$APP_BUNDLE/Contents/MacOS/host"

# Code signing
ENTITLEMENTS="$PROJECT_PATH/TerminalHost.entitlements"
if [ -n "$CODESIGN_IDENTITY" ]; then
    echo "Code signing app bundle with identity: $CODESIGN_IDENTITY"
    if [ -f "$ENTITLEMENTS" ]; then
        codesign --force --deep --options runtime --entitlements "$ENTITLEMENTS" --sign "$CODESIGN_IDENTITY" "$APP_BUNDLE"
    else
        codesign --force --deep --options runtime --sign "$CODESIGN_IDENTITY" "$APP_BUNDLE"
    fi
    echo "Code signing complete."
else
    # Ad-hoc signing with entitlements (allows local use without Apple Developer account)
    echo "Ad-hoc code signing with entitlements..."
    if [ -f "$ENTITLEMENTS" ]; then
        codesign --force --deep --entitlements "$ENTITLEMENTS" --sign - "$APP_BUNDLE"
        echo "Ad-hoc signing complete. App can access Dropbox/cloud storage."
    else
        echo "Warning: No entitlements file found. App may have limited file access."
    fi
fi

echo ""
echo "==================================="
echo "App bundle created successfully!"
echo "App bundle: $APP_BUNDLE"
echo "Architecture: $RUNTIME"
echo "==================================="

# Create DMG if requested
if [ "$CREATE_DMG" = true ]; then
    echo ""
    echo "Creating DMG installer..."

    DMG_NAME="${APP_NAME}-${VERSION}-${ARCH}.dmg"
    DMG_PATH="$OUTPUT_DIR/$DMG_NAME"
    DMG_TEMP="$OUTPUT_DIR/dmg-temp"

    # Clean up any previous DMG artifacts
    rm -f "$DMG_PATH"
    rm -rf "$DMG_TEMP"

    # Create temp directory for DMG contents
    mkdir -p "$DMG_TEMP"

    # Copy app bundle to temp directory
    cp -R "$APP_BUNDLE" "$DMG_TEMP/"

    # Create symbolic link to /Applications
    ln -s /Applications "$DMG_TEMP/Applications"

    # Create DMG
    echo "Building DMG..."
    hdiutil create -volname "$APP_NAME" \
        -srcfolder "$DMG_TEMP" \
        -ov -format UDZO \
        "$DMG_PATH"

    # Clean up temp directory
    rm -rf "$DMG_TEMP"

    # Sign DMG if code signing is available
    if [ -n "$CODESIGN_IDENTITY" ]; then
        echo "Code signing DMG..."
        codesign --force --sign "$CODESIGN_IDENTITY" "$DMG_PATH"
    fi

    echo ""
    echo "==================================="
    echo "DMG installer created!"
    echo "DMG: $DMG_PATH"
    echo "==================================="
fi

echo ""
echo "To run the app:"
echo "  open $APP_BUNDLE"
echo ""
echo "To install to Applications:"
echo "  cp -R $APP_BUNDLE /Applications/"
if [ "$CREATE_DMG" = true ]; then
    echo ""
    echo "Or use the DMG installer:"
    echo "  open $DMG_PATH"
fi
echo ""
