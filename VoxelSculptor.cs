using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Common.Voxels
{
#if UNITY_EDITOR
	[CanEditMultipleObjects]
	[ExecuteInEditMode]
	[RequireComponent(typeof(MeshFilter))]
	[RequireComponent(typeof(MeshRenderer))]
	[RequireComponent(typeof(MeshCollider))]
	public class VoxelSculptor : MonoBehaviour
	{
		private readonly Color RECTANGLE_GIZMO_COLOR = new Color(1.0f, 1.0f, 1.0f, 32.0f / 255.0f);
		private readonly Color RECTANGLE_GIZMO_OUTLINE = new Color(0.0f, 0.0f, 0.0f, 128.0f / 255.0f);

		[SerializeField] [SelectableField(0.1f, 0.25f, 0.5f, 1.0f)] private float m_Scale;
		[SerializeField] private Color32 m_Color;

		[SerializeField] private List<Vector3Int> m_Indices = new List<Vector3Int>();
		[SerializeField] private List<Color32> m_Colors = new List<Color32>();

		[SerializeField] [HideInInspector] private Bool3 m_MirrorAxes;

#pragma warning disable 0649
		private bool m_IsSculpting;
		private bool m_IsPainting;
		private bool m_IsDirty;
#pragma warning restore

		private void AddIndex(Vector3Int index)
		{
			if (m_Indices.AddUnique(index))
			{
				for (int d = 0; d < CubeUtility.DIRECTIONS.Length; d++)
				{
					m_Colors.Add(m_Color);
				}
			}
		}

		private void RemoveIndex(Vector3Int index)
		{
			if (m_Indices.TryGetIndex(index, out int i))
			{
				m_Indices.RemoveAt(i);
				m_Colors.RemoveRange(i * CubeUtility.DIRECTIONS.Length, CubeUtility.DIRECTIONS.Length);
			}
		}

		private Mesh CreateNewCurrentMesh()
		{
			if (
				TryGetComponent(out MeshFilter meshFilter) &&
				TryGetComponent(out MeshCollider meshCollider)
			) {
				return meshCollider.sharedMesh = meshFilter.sharedMesh = new Mesh();
			}
			return null;
		}

		private void WriteToCurrentMesh()
		{
			if (
				TryGetComponent(out MeshFilter meshFilter) &&
				TryGetComponent(out MeshCollider meshCollider)
			) {
				var mesh = meshFilter.sharedMesh;

				var meshBuilder = new FlatMeshBuilder();

				for (int i = 0; i < m_Indices.Count; i++)
				{
					var index = m_Indices[i];

					for (int d = 0; d < CubeUtility.DIRECTIONS.Length; d++)
					{
						var direction = CubeUtility.DIRECTIONS[d];
						var neighbour = Vector3Int.RoundToInt(index + direction);

						if (!m_Indices.Contains(neighbour))
						{
							var c = i * CubeUtility.DIRECTIONS.Length + d;
							var color = m_Colors[c];

							var triangles = CubeUtility.TRIANGLES[d];
							for (int t = 0; triangles[t] != -1; t += 3)
							{
								var v0 = (index + CubeUtility.VERTICES[triangles[t + 0]]) * m_Scale;
								var v1 = (index + CubeUtility.VERTICES[triangles[t + 1]]) * m_Scale;
								var v2 = (index + CubeUtility.VERTICES[triangles[t + 2]]) * m_Scale;

								meshBuilder.AddTriangle(v0, v1, v2);
								meshBuilder.AddColors(color, color, color);
							}
						}
					}
				}

				meshBuilder.Overwrite(mesh);

				meshCollider.sharedMesh = mesh; // Only because mesh has to be updated in physics cache

				m_IsDirty = true;
			}
		}

		private void ReadFromCurrentMesh()
		{
			if (TryGetComponent(out MeshFilter meshFilter))
			{
				var mesh = meshFilter.sharedMesh;

				m_Indices.Clear();
				m_Colors.Clear();

				var normals = mesh.normals;
				var vertices = mesh.vertices;
				var colors = mesh.colors32;

				for (int v = 0; v < vertices.Length; v += TriangleUtility.VCOUNT)
				{
					var minDistance = float.MaxValue;
					var maxDistance = float.MinValue;
					var maxv0 = Vector3.zero;
					var maxv1 = Vector3.zero;

					for (int i0 = 0; i0 < TriangleUtility.VCOUNT; i0++)
					{
						var i1 = Mathx.NextIndex(i0, TriangleUtility.VCOUNT);

						var v0 = vertices[v + i0];
						var v1 = vertices[v + i1];

						var distance = Vector3.SqrMagnitude(v1 - v0);
						if (minDistance > distance)
						{
							minDistance = distance;
						}
						if (maxDistance < distance)
						{
							maxDistance = distance;
							maxv0 = v0;
							maxv1 = v1;
						}
					}

					var normal = normals[v + 1];
					var color = colors[v + 1];

					var index = Vector3Int.RoundToInt((maxv0 + maxv1) * 0.5f / minDistance - normal * 0.5f);

					m_Scale = Mathf.Min(m_Scale, minDistance);

					AddIndex(index);

					var direction = Vector3Int.RoundToInt(normal);
					if (CubeUtility.DIRECTIONS.TryGetIndex(direction, out int d))
					{
						var c = (m_Indices.Count - 1) * CubeUtility.DIRECTIONS.Length + d;
						m_Colors[c] = color;
					}
				}
			}
		}
		
		public bool CanSave()
		{
			return m_IsDirty;
		}

		public void Save()
		{
			if (TryGetComponent(out MeshFilter meshFilter))
			{
				var mesh = meshFilter.sharedMesh;

				var path = AssetDatabase.GetAssetPath(mesh);

				if (string.IsNullOrEmpty(path))
				{
					const string DEFAULT_NAME = "";
					const string DEFAULT_MESSAGE = "";
					path = EditorUtility.SaveFilePanelInProject("Save Mesh", DEFAULT_NAME, "asset", DEFAULT_MESSAGE);

					AssetDatabase.CreateAsset(mesh, path);
				}

				AssetDatabase.SaveAssets();
			}

			m_IsDirty = false;
		}

		public bool IsSculpting()
		{
			return m_IsSculpting;
		}

		public string BeginSculpting()
		{
			if (m_IsSculpting)
				return "Couldn't begin sculpting. Already sculpting";
			
			m_IsSculpting = true;
			return null;
		}

		public string EndSculpting()
		{
			if (!m_IsSculpting)
				return "Couldn't end sculpting. Not sculpting";
			
			m_IsSculpting = false;
			return null;
		}

		public bool IsPainting()
		{
			return m_IsPainting;
		}

		public string BeginPainting()
		{
			if (m_IsPainting)
				return "Couldn't begin painting. Already painting";

			m_IsPainting = true;
			return null;
		}

		public string EndPainting()
		{
			if (!m_IsPainting)
				return "Couldn't end painting. Not painting";

			m_IsPainting = false;
			return null;
		}

		public void OnSceneGUI()
		{
			const int LEFT_MOUSE_BUTTON = 0;
			var currentEvent = Event.current;

			switch (currentEvent.type)
			{
				case EventType.Layout:
					HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
					break;

				case EventType.Repaint:
					DrawRectangleGizmo(currentEvent.mousePosition);
					if (m_IsSculpting)
						DrawMirrorAxesGizmo();
					break;

				case EventType.MouseMove:
					HandleUtility.Repaint();
					break;

				case EventType.MouseDown:
					if (currentEvent.button == LEFT_MOUSE_BUTTON)
					{
						if (m_IsSculpting)
							Sculpt(currentEvent.mousePosition, currentEvent.shift);
						else if (m_IsPainting)
							Paint(currentEvent.mousePosition);
						currentEvent.Use();
					}
					break;

				case EventType.MouseDrag:
					if (currentEvent.button == LEFT_MOUSE_BUTTON)
					{
						if (m_IsPainting)
							Paint(currentEvent.mousePosition);
						currentEvent.Use();
					}
					break;
			}
		}

		private void DrawRectangleGizmo(Vector2 mousePosition)
		{
			var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit))
			{
				var hitPoint = hit.point;
				var hitNormal = hit.normal;

				var hitOffset = hitNormal * 0.5f;

				var hitPointGrided = (Mathx.Round(hitPoint / m_Scale + hitOffset) - hitOffset) * m_Scale;
				var rect3D = RectUtility.GetRect3D(hitPointGrided, hitNormal, m_Scale, m_Scale);

				Handles.zTest = CompareFunction.Always;
				Handles.DrawSolidRectangleWithOutline(rect3D, RECTANGLE_GIZMO_COLOR, RECTANGLE_GIZMO_OUTLINE);
			}
		}

		private void DrawMirrorAxesGizmo()
		{
			if (m_MirrorAxes.Any())
			{
				var range = new Range3Int(Vector3Int.one * int.MaxValue, Vector3Int.one * int.MinValue);
				foreach (var index in m_Indices)
				{
					range.min = Mathx.Min(range.min, index);
					range.max = Mathx.Max(range.max, index);
				}

				var center = range.Center * m_Scale;
				var extents = Mathx.Multiply((range.Extents + Vector3Int.one * 2), m_Scale);

				for (int i = 0; i < 3; ++i)
				{
					if (m_MirrorAxes[i])
					{
						var selector = new Bool3(i == 0, i == 1, i == 2);
						var axisCenter = Mathx.Multiply(center, Mathx.Select(Vector3.one, Vector3.zero, selector));
						var axisNormal = Mathx.Select(Vector3.zero, Vector3.one, selector);
						var rectSize = new Vector2(i == 0 ? extents.y : extents.x, i == 2 ? extents.y : extents.z);
						var rect3D = RectUtility.GetRect3D(axisCenter, axisNormal, rectSize);

						Handles.zTest = CompareFunction.Less;
						Handles.DrawSolidRectangleWithOutline(rect3D, new Color(axisNormal.x, axisNormal.y, axisNormal.z, 32.0f / 255.0f), RECTANGLE_GIZMO_OUTLINE);
					}
				}
			}
		}
		
		private void Sculpt(Vector2 mousePosition, bool remove)
		{
			var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit))
			{
				const int AXES_COUNT = 3;

				var hitPoint = hit.point;
				var hitNormal = hit.normal;

				var localHitPoint = this.transform.InverseTransformPoint(hitPoint);
				var hitOffset = hitNormal * 0.5f;

				if (remove)
				{
					var index = Vector3Int.RoundToInt(localHitPoint / m_Scale - hitOffset);

					RemoveIndex(index);

					void TryRemoveMirrorVoxel()
					{
						if (m_MirrorAxes.x && m_MirrorAxes.y && m_MirrorAxes.z)
						{
							var mirror = index;
							mirror *= -1;

							RemoveIndex(mirror);
						}
					}

					void TryRemoveMirrorVoxelByAxis(int i)
					{
						if (m_MirrorAxes[i] && index[i] != 0)
						{
							var mirror = index;
							mirror[i] *= -1;

							RemoveIndex(mirror);
						}
					}

					void TryRemoveMirrorVoxelByAxes(int a, int b)
					{
						if (m_MirrorAxes[a] && m_MirrorAxes[b] && (index[a] != 0 || index[b] != 0))
						{
							var mirror = index;
							mirror[a] *= -1;
							mirror[b] *= -1;

							RemoveIndex(mirror);
						}
					}

					TryRemoveMirrorVoxel();
					for (int i = 0; i < AXES_COUNT; i++)
					{
						TryRemoveMirrorVoxelByAxis(i);
						TryRemoveMirrorVoxelByAxes(i, Mathx.NextIndex(i, AXES_COUNT));
					}
				}
				else
				{
					var index = Vector3Int.RoundToInt(localHitPoint / m_Scale + hitOffset);

					AddIndex(index);

					void TryCreateMirrorVoxel()
					{
						if (m_MirrorAxes.x && m_MirrorAxes.y && m_MirrorAxes.z)
						{
							var mirror = index;
							mirror *= -1;

							AddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxis(int i)
					{
						if (m_MirrorAxes[i] && index[i] != 0)
						{
							var mirror = index;
							mirror[i] *= -1;

							AddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxes(int a, int b)
					{
						if (m_MirrorAxes[a] && m_MirrorAxes[b] && (index[a] != 0 || index[b] != 0))
						{
							var mirror = index;
							mirror[a] *= -1;
							mirror[b] *= -1;

							AddIndex(mirror);
						}
					}

					TryCreateMirrorVoxel();
					for (int i = 0; i < AXES_COUNT; i++)
					{
						TryCreateMirrorVoxelByAxis(i);
						TryCreateMirrorVoxelByAxes(i, Mathx.NextIndex(i, AXES_COUNT));
					}
				}

				WriteToCurrentMesh();
			}
		}

		private void Paint(Vector2 mousePosition)
		{
			var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit))
			{
				var hitPoint = hit.point;
				var hitNormal = hit.normal;

				var localHitPoint = this.transform.InverseTransformPoint(hitPoint);
				var hitOffset = hitNormal * 0.5f;

				var index = Vector3Int.RoundToInt(localHitPoint / m_Scale - hitOffset);
				var direction = Vector3Int.RoundToInt(hitNormal);

				if (
					m_Indices.TryGetIndex(index, out int i) &&
					CubeUtility.DIRECTIONS.TryGetIndex(direction, out int d)
				) {
					var c = i * CubeUtility.DIRECTIONS.Length + d;
					var currentColor = m_Colors[c];

					if (Utility.TryUpdate(ref currentColor, m_Color))
					{
						m_Colors[c] = currentColor;

						WriteToCurrentMesh();
					}
				}
			}
		}

		public string Flip(bool x = false, bool y = false, bool z = false)
		{
			var selector = new Bool3(x, y, z);
			var flipper = Mathx.Select(Vector3Int.one, -Vector3Int.one, selector);

			for (int i = 0; i < m_Indices.Count; i++)
			{
				m_Indices[i] = m_Indices[i] * flipper;
			}

			return null;
		}

#if UNITY_EDITOR
		private Mesh m_CachedMesh;
		private float m_CachedScale;

		private void Update()
		{
			if (TryGetComponent(out MeshFilter meshFilter) && TryGetComponent(out MeshCollider meshCollider))
			{
				if (Utility.TryUpdate(ref m_CachedMesh, meshFilter.sharedMesh))
				{
					if (m_CachedMesh == null)
					{
						m_CachedMesh = CreateNewCurrentMesh();
						WriteToCurrentMesh();
					}
					else
					{
						ReadFromCurrentMesh();
					}
				}

				if (Utility.TryUpdate(ref m_CachedScale, m_Scale))
				{
					WriteToCurrentMesh();
				}
			}
		}

		private void Reset()
		{
			m_Scale = 1.0f;
			m_Color = ColorUtility.WHITE;

			m_Indices.Clear();
			m_Colors.Clear();

			m_MirrorAxes = Bool3.False;

			AddIndex(Vector3Int.zero);
			
			WriteToCurrentMesh();
		}
#endif
	}
#else
	public class VoxelSculptor : MonoBehaviour
	{
	}
#endif
}