using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace MotelGame.Editor
{
    // All development build menu entries delegate here, so old milestone folders never return.
    public static class PlaytestBuild
    {
        public const string Output="Builds/Playtest";
        [MenuItem("Tools/Motel/Build Current Playtest")]
        public static void Build() => BuildTo(Output);
        public static void BuildTo(string output)
        {
            Directory.CreateDirectory(output);
            var snapshots=new Dictionary<string,byte[]>();
            foreach(var path in new[]{MultipleGuestsSetup.ScenePath,"Assets/Settings/Mobile_RPAsset.asset","Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})
                if(File.Exists(path))snapshots[path]=File.ReadAllBytes(path);
            try
            {
                MultipleGuestsSetup.Validate();CharacterVisualsSetup.Validate();GuestCheckoutSetup.Validate();NightShiftSetup.Validate();MotelOperationsSetup.Validate();MotelVisualOverhaul.Validate();OnlineMultiplayerSetup.Validate();MimicHuntSetup.Validate();BaggageInspectionSetup.Validate();
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{MultipleGuestsSetup.ScenePath},locationPathName=output+"/MotelGame.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText("Documentation/BaggageInspectionM15/build-validation.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nOutput: "+output+"/MotelGame.app\n");
                if(report.summary.result!=BuildResult.Succeeded || report.summary.totalErrors > 0 || !File.Exists(output+"/MotelGame.app/Contents/MacOS/MotelGame"))throw new Exception("Playtest build failed.");
                File.WriteAllText(output+"/LaunchWindowed.command","#!/bin/zsh\nopen -n \"${0:A:h}/MotelGame.app\" --args -screen-fullscreen 0 -screen-width 800 -screen-height 600\n");
                for (int i = 1; i <= 4; i++)
                {
                    string launcher = output + "/LocalTestPlayer" + i + ".command";
                    File.WriteAllText(launcher, "#!/bin/zsh\nopen -n \"${0:A:h}/MotelGame.app\" --args -motel-profile playtest" + i + " -screen-fullscreen 0 -screen-width 800 -screen-height 600\n");
                    using (var mode = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/chmod", "755 " + launcher) { UseShellExecute = false })) mode.WaitForExit();
                }
                using(var chmod=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/chmod","755 "+output+"/LaunchWindowed.command"){UseShellExecute=false}))chmod.WaitForExit();
            }
            finally{foreach(var pair in snapshots)File.WriteAllBytes(pair.Key,pair.Value);AssetDatabase.Refresh();}
        }
        [MenuItem("Tools/Motel/Online/Build Windows Playtest")]
        public static void BuildWindows()
        {
            OnlineMultiplayerSetup.Validate();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows Mono Build Support is not loaded. Restart the Editor after installing the module.");
            string folder = Output + "/Windows";
            Directory.CreateDirectory(folder);
            var snapshots = new Dictionary<string, byte[]>();
            foreach (string path in new[] { MultipleGuestsSetup.ScenePath, "ProjectSettings/ProjectSettings.asset", "ProjectSettings/UnityConnectSettings.asset", "ProjectSettings/EditorBuildSettings.asset", "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset", "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/DefaultVolumeProfile.asset" })
                if (File.Exists(path)) snapshots[path] = File.ReadAllBytes(path);
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { MultipleGuestsSetup.ScenePath }, locationPathName = folder + "/MotelGame.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
                File.WriteAllText(OnlineMultiplayerSetup.Reports + "/windows-build-validation.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\n");
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0 || !File.Exists(folder + "/MotelGame.exe")) throw new Exception("Windows build failed; inspect windows-build-validation.txt and Editor log.");
                File.WriteAllText(folder + "/LaunchWindowed.bat", "@echo off\r\nstart \"\" \"%~dp0MotelGame.exe\" -screen-fullscreen 0 -screen-width 800 -screen-height 600\r\n");
                for (int i = 1; i <= 4; i++)
                    File.WriteAllText(folder + "/LocalTestPlayer" + i + ".bat", "@echo off\r\nstart \"\" \"%~dp0MotelGame.exe\" -motel-profile playtest" + i + " -screen-fullscreen 0 -screen-width 800 -screen-height 600\r\n");
            }
            finally { foreach (var pair in snapshots) File.WriteAllBytes(pair.Key, pair.Value); AssetDatabase.Refresh(); }
        }
    }
}
