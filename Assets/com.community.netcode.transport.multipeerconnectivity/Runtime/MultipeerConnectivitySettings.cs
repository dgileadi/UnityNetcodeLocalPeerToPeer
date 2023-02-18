using UnityEditor;
using UnityEngine;

namespace Netcode.Transports.MultipeerConnectivity
{

    public class MultipeerConnectivitySettings : ScriptableObject
    {
        public const string MultipeerConnectivitySettingsPath = "Assets/MultipeerConnectivitySettings.asset";
        public const string NetworkUsageDescriptionTooltip = "String shown to the user when requesting permission to access the local network. Written to the NSLocalNetworkUsageDescription field in Xcode project's info.plist file.";
        public const string BonjourServiceTypeTooltip = "A Bonjour application ID such as \"_my-app\". Written to the NSBonjourServices field in Xcode project's info.plist file with \"._udp\" and \"._tcp\" suffixes. It should be 15 characters or less and can contain ASCII, lowercase letters, numbers, and hyphens.";

        public string NetworkUsageDescription => m_NetworkUsageDescription;

        /// See <a href="https://developer.apple.com/documentation/multipeerconnectivity/mcnearbyserviceadvertiser">MCNearbyServiceAdvertiser</a>
        /// for the purpose of and restrictions on this name.
        public string BonjourServiceType => m_BonjourServiceType;

        [SerializeField]
        [Tooltip(NetworkUsageDescriptionTooltip)]
        private string m_NetworkUsageDescription;

        [SerializeField]
        [Tooltip(BonjourServiceTypeTooltip)]
        private string m_BonjourServiceType;

        public static MultipeerConnectivitySettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MultipeerConnectivitySettings>(MultipeerConnectivitySettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MultipeerConnectivitySettings>();
                settings.m_NetworkUsageDescription = null;
                settings.m_BonjourServiceType = null;
                AssetDatabase.CreateAsset(settings, MultipeerConnectivitySettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }
    }

}