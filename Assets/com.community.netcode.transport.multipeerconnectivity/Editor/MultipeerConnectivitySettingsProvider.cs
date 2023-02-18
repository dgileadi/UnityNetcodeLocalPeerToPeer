using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Netcode.Transports.MultipeerConnectivity
{

    static class MultipeerConnectivitySettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateMultipeerSettingsProvider()
        {
            return new SettingsProvider("Project/MultipeerSettings", SettingsScope.Project)
            {
                label = "Multipeer Connectivity",
                guiHandler = (searchContext) =>
                {
                    var settings = new SerializedObject(MultipeerConnectivitySettings.GetOrCreateSettings());
                    EditorGUILayout.PropertyField(settings.FindProperty("m_NetworkUsageDescription"), new GUIContent("Network Usage Description", MultipeerConnectivitySettings.NetworkUsageDescriptionTooltip));
                    EditorGUILayout.PropertyField(settings.FindProperty("m_BonjourServiceType"), new GUIContent("Bonjour App ID", MultipeerConnectivitySettings.BonjourServiceTypeTooltip));
                    settings.ApplyModifiedPropertiesWithoutUndo();
                },
                keywords = new HashSet<string>(new[] { "Network Usage Description", "Bonjour Service Type" })
            };
        }
    }

}