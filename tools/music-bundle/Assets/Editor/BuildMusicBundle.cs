using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the mod's run music (src/plugin/Music/*.mp3) into src/plugin/Assets/megaphonk-music.bundle. The game strips
/// Unity's audio file loaders, so the mod loads its tracks as AudioClips of an AssetBundle, like the game's own music.
/// Must be built with the game's Unity version (2023.2.22f1): see build.sh
/// </summary>
public static class BuildMusicBundle
{
    private const string MUSIC_ASSETS = "Assets/Music";

    public static void Build()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        var source = Path.Combine(root, "src/plugin/Music");
        var output = Path.Combine(root, "src/plugin/Assets");

        // Fresh copy of the tracks
        var target = Path.Combine(Application.dataPath, "Music");
        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*.mp3"))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        AssetDatabase.Refresh();

        var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { MUSIC_ASSETS }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        foreach (var path in paths)
        {
            // Streamed from the bundle (a track takes little memory), Vorbis like the game's music
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        var temp = Path.Combine(Path.GetTempPath(), "megaphonk-music-bundle");
        Directory.CreateDirectory(temp);
        var build = new AssetBundleBuild { assetBundleName = "megaphonk-music.bundle", assetNames = paths };
        // Uncompressed: the clips are Vorbis already, and streaming reads them straight from the file
        BuildPipeline.BuildAssetBundles(temp, new[] { build }, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
        File.Copy(Path.Combine(temp, "megaphonk-music.bundle"), Path.Combine(output, "megaphonk-music.bundle"), true);
        Debug.Log($"Music bundle: {paths.Length} track(s) -> {output}");
    }
}
