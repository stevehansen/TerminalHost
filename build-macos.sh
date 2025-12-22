#!/bin/bash
# Build script for macOS app bundle

set -e

# Configuration
APP_NAME="TerminalHost"
BUNDLE_ID="com.terminalhost.app"
VERSION="1.0.0"
PROJECT_PATH="src/TerminalHost/TerminalHost"
OUTPUT_DIR="publish"

# Determine architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RUNTIME="osx-arm64"
else
    RUNTIME="osx-x64"
fi

echo "Building TerminalHost for macOS ($ARCH)..."

# Clean previous build
rm -rf "$OUTPUT_DIR"

# Build the project
echo "Publishing .NET project..."
dotnet publish "$PROJECT_PATH" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -o "$OUTPUT_DIR/$RUNTIME"

# Create app bundle structure
echo "Creating app bundle structure..."
APP_BUNDLE="$OUTPUT_DIR/${APP_NAME}.app"
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
    <string>app</string>
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
fi

# Make executable
chmod +x "$APP_BUNDLE/Contents/MacOS/host"

echo ""
echo "==================================="
echo "Build completed successfully!"
echo "App bundle: $APP_BUNDLE"
echo "Architecture: $RUNTIME"
echo "==================================="
echo ""
echo "To run the app:"
echo "  open $APP_BUNDLE"
echo ""
echo "To install to Applications:"
echo "  cp -R $APP_BUNDLE /Applications/"
echo ""

# Optional: Code signing (requires Apple Developer account)
if [ -n "$CODESIGN_IDENTITY" ]; then
    echo "Code signing app bundle..."
    codesign --force --deep --sign "$CODESIGN_IDENTITY" "$APP_BUNDLE"
    echo "Code signing complete."
fi
