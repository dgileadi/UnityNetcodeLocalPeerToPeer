using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

// Copied from https://stackoverflow.com/questions/54370336/from-unity-to-ios-how-to-perfectly-automate-frameworks-settings-and-plist/54370793#54370793

namespace Netcode.Transports.MultipeerConnectivity
{

    public class BuildPostProcessor
    {
        [PostProcessBuild]
        public static void ChangeXcodePlist(BuildTarget buildTarget, string path)
        {
            var multipeerSettings = MultipeerConnectivitySettings.GetOrCreateSettings();
            if (string.IsNullOrEmpty(multipeerSettings.NetworkUsageDescription) || string.IsNullOrEmpty(multipeerSettings.BonjourServiceType))
            {
                Debug.LogError("Multipeer Connectivity for Netcode for GameObjects is missing required settings. Please provide them in the Multipeer Connectivity section of your Project Settings.");
                return;
            }

            if (buildTarget == BuildTarget.iOS)
            {
                string plistPath = path + "/Info.plist";
                PlistDocument plist = new PlistDocument();
                plist.ReadFromFile(plistPath);

                PlistElementDict rootDict = plist.root;

                Debug.Log(">> Automation, plist ... <<");

                rootDict.SetString("NSLocalNetworkUsageDescription", multipeerSettings.NetworkUsageDescription);

                var bonjourKeys = rootDict.CreateArray("NSBonjourServices");
                bonjourKeys.AddString(multipeerSettings.BonjourServiceType + "._udp");
                bonjourKeys.AddString(multipeerSettings.BonjourServiceType + "._tcp");

                File.WriteAllText(plistPath, plist.WriteToString());
            }
        }
    }

}
