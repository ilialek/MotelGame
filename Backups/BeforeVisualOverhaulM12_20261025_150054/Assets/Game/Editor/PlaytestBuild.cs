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
        public static void Build()
        {
            MultipleGuestsSetup.Validate();CharacterVisualsSetup.Validate();GuestCheckoutSetup.Validate();NightShiftSetup.Validate();MotelOperationsSetup.Validate();Directory.CreateDirectory(Output);
            var snapshots=new Dictionary<string,byte[]>();
            foreach(var path in new[]{"Assets/Settings/Mobile_RPAsset.asset","Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})
                if(File.Exists(path))snapshots[path]=File.ReadAllBytes(path);
            try
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{MultipleGuestsSetup.ScenePath},locationPathName=Output+"/MotelGame.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText(MotelOperationsSetup.Reports+"/build-validation.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nOutput: "+Output+"/MotelGame.app\n");
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Playtest build failed.");
                File.WriteAllText(Output+"/LaunchWindowed.command","#!/bin/zsh\nopen -n \"${0:A:h}/MotelGame.app\" --args -screen-fullscreen 0 -screen-width 800 -screen-height 600\n");
                using(var chmod=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/chmod","755 "+Output+"/LaunchWindowed.command"){UseShellExecute=false}))chmod.WaitForExit();
            }
            finally{foreach(var pair in snapshots)File.WriteAllBytes(pair.Key,pair.Value);AssetDatabase.Refresh();}
        }
    }
}
