using UnityEditor;
using Common.Voxels;
using UnityEngine;

namespace CommonEditor.Voxels
{
	[CustomEditor(typeof(VoxelSculptor))]
	public class VoxelSculptorEditor : Editor
	{
		private string _error;

		private SerializedProperty _scaleProperty;
		private SerializedProperty _colorProperty;
		private SerializedProperty _mirrorAxesProperty;

		private VoxelSculptor Script
        {
			get => (VoxelSculptor)target;
        }
		
		private void OnEnable()
		{
			_scaleProperty = serializedObject.FindProperty("_scale");
			_colorProperty = serializedObject.FindProperty("_color");
			_mirrorAxesProperty = serializedObject.FindProperty("_mirrorAxes");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			if (!string.IsNullOrEmpty(_error))
			{
				EditorGUILayout.HelpBox(_error, MessageType.Error);
			}

			EditorGUILayout.PropertyField(_scaleProperty);

			if (Script.IsSculpting())
			{
				GUI.color = Color.green;
				if (GUILayout.Button("End Sculpting"))
				{
					_error = Script.EndSculpting();
				}
				GUI.color = Color.white;

				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Flip X"))
				{
					_error = Script.Flip(x: true);
				}
				if (GUILayout.Button("Flip Y"))
				{
					_error = Script.Flip(y: true);
				}
				if (GUILayout.Button("Flip Z"))
				{
					_error = Script.Flip(z: true);
				}
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.PropertyField(_mirrorAxesProperty);
			}
			else if (Script.IsPainting())
			{
				GUI.color = Color.green;
				if (GUILayout.Button("End Painting"))
				{
					_error = Script.EndPainting();
				}
				GUI.color = Color.white;

				EditorGUILayout.PropertyField(_colorProperty);
			}
			else
			{
				if (GUILayout.Button("Begin Sculpting"))
				{
					_error = Script.BeginSculpting();
				}

				if (GUILayout.Button("Begin Painting"))
				{
					_error = Script.BeginPainting();
				}

				if (Script.CanSave())
				{
					if (GUILayout.Button("Save"))
					{
						Script.Save();
					}
				}
			}
			
			serializedObject.ApplyModifiedProperties();
		}

		private void OnSceneGUI()
		{
			if (Script.IsSculpting() || Script.IsPainting())
			{
				Script.OnSceneGUI();
			}
		}
	}
}