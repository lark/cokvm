killall -9 CoKVM 2>/dev/null; sleep 1;

PROJECT_DIR="${PWD}"
APP_NAME="CoKVM"
APP_BUNDLE="${PROJECT_DIR}/${APP_NAME}.app"

if [ ! -d "${PROJECT_DIR}/${APP_NAME}" ]; then
	echo "Wrong directory"
	exit
fi

BUILD_CONFIG="debug"
if [ "$1" = "release" ]; then
    BUILD_CONFIG="release"
fi

swift build --disable-sandbox -c "${BUILD_CONFIG}" || exit

rm -rf "${APP_BUNDLE}"

mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

cp .build/arm64-apple-macosx/"${BUILD_CONFIG}"/${APP_NAME} "${APP_BUNDLE}/Contents/MacOS/"
cp CoKVM/Info.plist "${APP_BUNDLE}/Contents/"
cp -R Resources/*.lproj "${APP_BUNDLE}/Contents/Resources/"

chmod +x "${APP_BUNDLE}/Contents/MacOS/${APP_NAME}"
codesign --force --deep --sign - "${APP_BUNDLE}"

echo "Built: ${APP_BUNDLE} (${BUILD_CONFIG})"
