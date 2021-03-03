using System.Collections.Generic;
using System.Linq;
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

		[SerializeField] private float m_Scale;
		[SerializeField] private Color32 m_Color;

		[SerializeField] /*[HideInInspector]*/ private List<Vector3Int> m_Indices = new List<Vector3Int>();
		[SerializeField] /*[HideInInspector]*/ private List<Color32> m_Colors = new List<Color32>();

		[SerializeField] [HideInInspector] private Bool3 m_MirrorAxes;

#pragma warning disable 0649
		private bool m_IsSculpting;
		private bool m_IsPainting;
		private bool m_IsDirty;
#pragma warning restore

		private bool TryAddIndex(Vector3Int index)
		{
			var added = m_Indices.AddUnique(index);
			if (added)
			{
				for (int d = 0; d < CartesianUtility.Directions3D.Length; d++)
				{
					m_Colors.Add(m_Color);
				}
			}
			return added;
		}

		private void RemoveIndex(Vector3Int index)
		{
			if (m_Indices.TryIndexOf(index, out int i))
			{
				m_Indices.RemoveAt(i);
				m_Colors.RemoveRange(i * CartesianUtility.Directions3D.Length, CartesianUtility.Directions3D.Length);
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

					for (int d = 0; d < CartesianUtility.Directions3D.Length; d++)
					{
						var direction = CartesianUtility.Directions3D[d];
						var neighbour = Vector3Int.RoundToInt(index + direction);

						if (!m_Indices.Contains(neighbour))
						{
							var c = i * CartesianUtility.Directions3D.Length + d;
							var color = m_Colors[c];

							var triangles = CubeUtility.Triangles[d];
							for (int t = 0; triangles[t] != -1; t += 3)
							{
								var v0 = (index + CubeUtility.Vertices[triangles[t + 0]]) * m_Scale;
								var v1 = (index + CubeUtility.Vertices[triangles[t + 1]]) * m_Scale;
								var v2 = (index + CubeUtility.Vertices[triangles[t + 2]]) * m_Scale;

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

				var bounds = new Range3Int(Vector3Int.one * int.MaxValue, Vector3Int.one * int.MinValue);
				var indicesNormals = new Dictionary<Vector3Int, HashSet<Vector3Int>>();

				var normals = mesh.normals;
				var vertices = mesh.vertices;
				var colors = mesh.colors32;

				for (int v = 0; v < vertices.Length; v += TriangleUtility.VCOUNT * 2)
				{
					var v0 = vertices[v + 0];
					var v1 = vertices[v + 1];
					var v2 = vertices[v + 2];

					var minDistance = Vector3.Magnitude(v1 - v0);
					var maxDistance = Vector3.Magnitude(v0 - v2);

					m_Scale = Mathf.Min(m_Scale, minDistance);

					var normal = Vector3Int.RoundToInt(normals[v + 1]);
					var index = Vector3Int.RoundToInt((v2 + v0) * 0.5f / minDistance - Mathx.Multiply(normal, 0.5f));
					TryAddIndex(index);
					
					if (CartesianUtility.Directions3D.TryIndexOf(normal, out int d))
					{
						var c = (m_Indices.Count - 1) * CartesianUtility.Directions3D.Length + d;
						m_Colors[c] = colors[v + 1];
					}

					bounds.min = Mathx.Min(bounds.min, index);
					bounds.max = Mathx.Max(bounds.max, index);

					if (!indicesNormals.ContainsKey(index))
						indicesNormals.Add(index, new HashSet<Vector3Int>());
					indicesNormals[index].Add(normal);
				}
				
				// Add fake indices inside mesh
				for (int z = bounds.min.z; z < bounds.max.z; z++)
				{
					for (int y = bounds.min.y; y < bounds.max.y; y++)
					{
						for (int x = bounds.min.x; x < bounds.max.x; x++)
						{
							var index = new Vector3Int(x, y, z);
							
							if (indicesNormals.TryGetValue(index, out HashSet<Vector3Int> indexNormals))
							{
								foreach (var axis in CartesianUtility.Axes3D)
								{
									if (!indexNormals.Contains(axis))
									{
										var nextIndex = index + axis;

										if (!indicesNormals.ContainsKey(nextIndex))
											indicesNormals.Add(nextIndex, new HashSet<Vector3Int>());

										TryAddIndex(nextIndex);
									}
								}
							}
						}
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

					TryAddIndex(index);

					void TryCreateMirrorVoxel()
					{
						if (m_MirrorAxes.x && m_MirrorAxes.y && m_MirrorAxes.z)
						{
							var mirror = index;
							mirror *= -1;

							TryAddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxis(int i)
					{
						if (m_MirrorAxes[i] && index[i] != 0)
						{
							var mirror = index;
							mirror[i] *= -1;

							TryAddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxes(int a, int b)
					{
						if (m_MirrorAxes[a] && m_MirrorAxes[b] && (index[a] != 0 || index[b] != 0))
						{
							var mirror = index;
							mirror[a] *= -1;
							mirror[b] *= -1;

							TryAddIndex(mirror);
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
					m_Indices.TryIndexOf(index, out int i) &&
					CartesianUtility.Directions3D.TryIndexOf(direction, out int d)
				) {
					var c = i * CartesianUtility.Directions3D.Length + d;
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
				m_Indices[i] *= flipper;

			WriteToCurrentMesh();

			return null;
		}

#if UNITY_EDITOR
		public bool showGizmos;

		private void OnDrawGizmos()
		{
			if (showGizmos)
			{
				var previousColor = Gizmos.color;
				Gizmos.color = Color.red;
				foreach (var index in m_Indices)
				{
					Gizmos.DrawSphere(index, m_Scale * 0.5f);
				}
				Gizmos.color = previousColor;
			}
		}

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
			m_Color = Color.white;

			m_Indices.Clear();
			m_Colors.Clear();

			m_MirrorAxes = Bool3.False;

			TryAddIndex(Vector3Int.zero);

			m_CachedMesh = CreateNewCurrentMesh();
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