using UnityEditor;
using Common.Voxels;
using UnityEngine;

namespace CommonEditor.Voxels
{
	[CustomEditor(typeof(VoxelSculptor))]
	public class VoxelSculptorEditor : Editor
	{
		private string m_Error;
		private VoxelSculptor m_Script;

		private SerializedProperty m_MirrorAxesProperty;
		
		private void OnEnable()
		{
			m_Script = (VoxelSculptor)target;

			m_MirrorAxesProperty = serializedObject.FindProperty("m_MirrorAxes");
		}

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			serializedObject.Update();

			if (!string.IsNullOrEmpty(m_Error))
			{
				EditorGUILayout.HelpBox(m_Error, MessageType.Error);
			}

			if (m_Script.IsSculpting())
			{
				GUI.color = Color.green;
				if (GUILayout.Button("End Sculpting"))
				{
					m_Error = m_Script.EndSculpting();
				}
				GUI.color = Color.white;

				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Flip X"))
				{
					m_Error = m_Script.Flip(x: true);
				}
				if (GUILayout.Button("Flip Y"))
				{
					m_Error = m_Script.Flip(y: true);
				}
				if (GUILayout.Button("Flip Z"))
				{
					m_Error = m_Script.Flip(z: true);
				}
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.PropertyField(m_MirrorAxesProperty);
			}
			else if (m_Script.IsPainting())
			{
				GUI.color = Color.green;
				if (GUILayout.Button("End Painting"))
				{
					m_Error = m_Script.EndPainting();
				}
				GUI.color = Color.white;
			}
			else
			{
				if (GUILayout.Button("Begin Sculpting"))
				{
					m_Error = m_Script.BeginSculpting();
				}

				if (GUILayout.Button("Begin Painting"))
				{
					m_Error = m_Script.BeginPainting();
				}

				if (m_Script.CanSave())
				{
					if (GUILayout.Button("Save"))
					{
						m_Script.Save();
					}
				}
			}
			
			serializedObject.ApplyModifiedProperties();
		}

		private void OnSceneGUI()
		{
			if (m_Script.IsSculpting() || m_Script.IsPainting())
			{
				m_Script.OnSceneGUI();
			}
		}
	}
}