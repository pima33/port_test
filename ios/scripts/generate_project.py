"""Generate the small native Xcode project using only Python's standard library.

Run after adding/removing Swift source files. No XcodeGen/CocoaPods dependencies.
"""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]


def uid(name):
    return hashlib.sha1(name.encode()).hexdigest()[:24].upper()


def quote(s):
    return json.dumps(str(s))


def generate():
    objects = []

    def obj(name, body):
        objects.append(f'{uid(name)} = {{ {body} }};')
        return uid(name)

    sources = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / 'Sources').rglob('*.swift'))
    files, builds = [], []
    for source in sources:
        ref = obj(source, f'isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = {quote(source)}; sourceTree = "<group>";')
        files.append(ref)
        builds.append(obj('build:' + source, f'isa = PBXBuildFile; fileRef = {ref};'))
    product = obj('product', 'isa = PBXFileReference; explicitFileType = wrapper.application; path = MagerCam.app; sourceTree = BUILT_PRODUCTS_DIR;')
    assets = obj('assets', 'isa = PBXFileReference; lastKnownFileType = folder.assetcatalog; path = Resources/Assets.xcassets; sourceTree = "<group>";')
    asset_build = obj('asset-build', f'isa = PBXBuildFile; fileRef = {assets};')
    plist = obj('plist', 'isa = PBXFileReference; lastKnownFileType = text.plist.xml; path = Resources/Info.plist; sourceTree = "<group>";')
    products = obj('products', f'isa = PBXGroup; children = ({product},); name = Products; sourceTree = "<group>";')
    group = obj('group', 'isa = PBXGroup; children = (' + ','.join(files + [assets, plist, products]) + ',); sourceTree = "<group>";')
    source_phase = obj('sources', 'isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = (' + ','.join(builds) + ',); runOnlyForDeploymentPostprocessing = 0;')
    resource_phase = obj('resources', f'isa = PBXResourcesBuildPhase; buildActionMask = 2147483647; files = ({asset_build},); runOnlyForDeploymentPostprocessing = 0;')
    framework_phase = obj('frameworks', 'isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0;')
    project_configs, target_configs = [], []
    for name in ['Debug', 'Release']:
        project_configs.append(obj('project:' + name, f'isa = XCBuildConfiguration; name = {name}; buildSettings = {{ SDKROOT = iphoneos; IPHONEOS_DEPLOYMENT_TARGET = 17.0; CLANG_ENABLE_MODULES = YES; }};'))
        settings = {
            'PRODUCT_BUNDLE_IDENTIFIER': 'com.magercam.ios', 'PRODUCT_NAME': 'MagerCam',
            'SWIFT_VERSION': '5.0', 'TARGETED_DEVICE_FAMILY': '1',
            'INFOPLIST_FILE': 'Resources/Info.plist', 'GENERATE_INFOPLIST_FILE': 'NO',
            'CODE_SIGN_STYLE': 'Automatic', 'CURRENT_PROJECT_VERSION': '1',
            'MARKETING_VERSION': '0.1.0', 'ASSETCATALOG_COMPILER_APPICON_NAME': 'AppIcon',
            'SWIFT_OPTIMIZATION_LEVEL': '-Onone' if name == 'Debug' else '-O',
            'DEBUG_INFORMATION_FORMAT': 'dwarf' if name == 'Debug' else 'dwarf-with-dsym',
            'LD_RUNPATH_SEARCH_PATHS': '$(inherited) @executable_path/Frameworks',
        }
        if name == 'Debug':
            settings['SWIFT_ACTIVE_COMPILATION_CONDITIONS'] = 'DEBUG'
        target_configs.append(obj('target:' + name, f'isa = XCBuildConfiguration; name = {name}; buildSettings = {{ ' + ' '.join(f'{key} = {quote(value)};' for key, value in settings.items()) + ' };'))
    project_list = obj('project-configs', 'isa = XCConfigurationList; buildConfigurations = (' + ','.join(project_configs) + ',); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release;')
    target_list = obj('target-configs', 'isa = XCConfigurationList; buildConfigurations = (' + ','.join(target_configs) + ',); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release;')
    target = obj('target', f'isa = PBXNativeTarget; buildConfigurationList = {target_list}; buildPhases = ({source_phase},{framework_phase},{resource_phase},); buildRules = (); dependencies = (); name = MagerCam; productName = MagerCam; productReference = {product}; productType = "com.apple.product-type.application";')
    project = obj('project', f'isa = PBXProject; attributes = {{ LastUpgradeCheck = 1660; }}; buildConfigurationList = {project_list}; compatibilityVersion = "Xcode 14.0"; developmentRegion = ru; hasScannedForEncodings = 0; knownRegions = (ru,en,Base,); mainGroup = {group}; productRefGroup = {products}; projectDirPath = ""; projectRoot = ""; targets = ({target},);')
    folder = ROOT / 'MagerCam.xcodeproj'
    folder.mkdir(exist_ok=True)
    (folder / 'project.pbxproj').write_text('// !$*UTF8*$!\n{ archiveVersion = 1; classes = {}; objectVersion = 56; objects = {\n' + '\n'.join(objects) + f'\n}}; rootObject = {project}; }}\n', encoding='utf8')
    schemes = folder / 'xcshareddata' / 'xcschemes'
    schemes.mkdir(parents=True, exist_ok=True)
    reference = f'<BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{target}" BuildableName="MagerCam.app" BlueprintName="MagerCam" ReferencedContainer="container:MagerCam.xcodeproj"/>'
    (schemes / 'MagerCam.xcscheme').write_text(f'''<?xml version="1.0" encoding="UTF-8"?>
<Scheme LastUpgradeVersion="1660" version="1.3">
 <BuildAction parallelizeBuildables="YES" buildImplicitDependencies="YES"><BuildActionEntries><BuildActionEntry buildForTesting="YES" buildForRunning="YES" buildForProfiling="YES" buildForArchiving="YES" buildForAnalyzing="YES">{reference}</BuildActionEntry></BuildActionEntries></BuildAction>
 <TestAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.IDEFoundation.Launcher.LLDB" shouldUseLaunchSchemeArgsEnv="YES"/>
 <LaunchAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.IDEFoundation.Launcher.LLDB" launchStyle="0" useCustomWorkingDirectory="NO" ignoresPersistentStateOnLaunch="NO" debugDocumentVersioning="YES" debugServiceExtension="internal" allowLocationSimulation="YES"><BuildableProductRunnable runnableDebuggingMode="0">{reference}</BuildableProductRunnable></LaunchAction>
 <ProfileAction buildConfiguration="Release" shouldUseLaunchSchemeArgsEnv="YES" useCustomWorkingDirectory="NO" debugDocumentVersioning="YES"><BuildableProductRunnable runnableDebuggingMode="0">{reference}</BuildableProductRunnable></ProfileAction>
 <AnalyzeAction buildConfiguration="Debug"/><ArchiveAction buildConfiguration="Release" revealArchiveInOrganizer="YES"/>
</Scheme>''', encoding='utf8')
    print(f'Generated project with {len(sources)} Swift files')


if __name__ == '__main__':
    generate()
