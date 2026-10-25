"""Builds the Thunderstore package of the mod (and publishes it with --publish).

The package holds the mod built with THUNDERSTORE (no self-update: Thunderstore packages must be updated by the mod
manager), the license, the icon and a README made from the English part of the repository's README, with mod manager
installation steps instead of the manual ones.

    python thunderstore/build.py            # build thunderstore/build/ROFL_MODS-MegaphonkMod-<version>.zip
    python thunderstore/build.py --publish  # build and upload (needs TCLI_AUTH_TOKEN, a Thunderstore service account token)
"""
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PLUGIN = os.path.join(ROOT, 'src', 'plugin')
GITHUB = 'https://github.com/coffeammaretto/megaphonk-mod'

INSTALL = f"""## Installation

Install with a mod manager ([r2modman](https://thunderstore.io/c/megabonk/p/ebkr/r2modman/) or [Gale](https://thunderstore.io/c/megabonk/p/Kesomannen/GaleModManager/)): it installs BepInEx too. Start the game from the mod manager.

Every player of a room needs the same mod version and must be on the host's Steam friends list. The mod manager updates the mod; the mod only tells you when a new version is out.

Manual installation (without a mod manager): see [GitHub]({GITHUB}#installation).

"""


def version():
    csproj = open(os.path.join(ROOT, 'src', 'plugin', 'Megaphonk.Plugin.csproj'), encoding='utf-8-sig').read()
    return re.search(r'<Version>([^<]+)</Version>', csproj).group(1)


def readme():
    text = open(os.path.join(ROOT, 'README.md'), encoding='utf-8-sig').read().replace('\r\n', '\n')
    english = text.split('\n## На русском')[0].rstrip().removesuffix('---').rstrip() + '\n'
    english = re.sub(r'\[Русская версия ниже\]\([^)]*\)\n\n', '', english)
    # Mod manager installation instead of the manual one
    english = re.sub(r'## Installation\n.*?(?=## Playing)', INSTALL, english, flags=re.S)
    credits = re.search(r'\n## Credits\n.*', english, flags=re.S).group(0).strip('\n')
    english = english.replace(credits, '').rstrip('\n')
    return english + f'\n\n## Source\n\nSource code, changelog and license: [GitHub]({GITHUB}).\n\n' + credits + '\n'


def tcli(*args):
    env = dict(os.environ, DOTNET_ROLL_FORWARD='Major')
    exe = shutil.which('tcli') or os.path.expanduser('~/.dotnet/tools/tcli.exe')
    subprocess.run([exe, *args, '--config-path', os.path.join(HERE, 'thunderstore.toml'),
                    '--package-version', version()], cwd=HERE, env=env, check=True)


def main():
    dist = os.path.join(HERE, 'dist')
    shutil.rmtree(dist, ignore_errors=True)
    # The user-local SDK first: a system-wide dotnet may be a runtime without an SDK
    dotnet = os.path.expanduser('~/.dotnet/dotnet.exe')
    if not os.path.exists(dotnet):
        dotnet = shutil.which('dotnet')
    subprocess.run([dotnet, 'build', os.path.join(PLUGIN, 'Megaphonk.Plugin.csproj'), '-c', 'Debug', '-p:PluginPath=',
                    '-p:THUNDERSTORE_BUILD=true', '-p:IntermediateOutputPath=obj/thunderstore/', '-o', dist], check=True)
    shutil.copy(os.path.join(ROOT, 'LICENSE'), os.path.join(dist, 'LICENSE.txt'))
    open(os.path.join(HERE, 'README.generated.md'), 'w', encoding='utf-8', newline='\n').write(readme())

    if '--publish' in sys.argv:
        if not os.environ.get('TCLI_AUTH_TOKEN'):
            sys.exit('TCLI_AUTH_TOKEN is not set')
        tcli('publish', '--token', os.environ['TCLI_AUTH_TOKEN'])
    else:
        tcli('build')


if __name__ == '__main__':
    main()
