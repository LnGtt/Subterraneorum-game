using UnityEditor;
using UnityEngine;

public class AudioImportSettings : AssetPostprocessor
{
    private const string MusicFolder = "Assets/Resources/Subterraneorum/Audio/Music/";
    private const string SfxFolder = "Assets/Resources/Subterraneorum/Audio/Sfx/";

    private void OnPreprocessAudio()
    {
        var importer = (AudioImporter)assetImporter;
        string path = assetPath.Replace('\\', '/');

        if (path.StartsWith(MusicFolder))
        {
            importer.forceToMono = false;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            settings.preloadAudioData = true;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
        else if (path.StartsWith(SfxFolder))
        {
            importer.forceToMono = false;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = true;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
