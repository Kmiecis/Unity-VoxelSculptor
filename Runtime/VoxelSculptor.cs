using Common.Extensions;
using Common.Mathematics;
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
		private readonly Color kRectGizmoColor = new Color(1.0f, 1.0f, 1.0f, 32.0f / 255.0f);
		private readonly Color kRectGizmoOutline = new Color(0.0f, 0.0f, 0.0f, 128.0f / 255.0f);

		[SerializeField] [HideInInspector]
		private List<Vector3Int> _indices = new List<Vector3Int>();
		[SerializeField] [HideInInspector]
		private List<Color32> _colors = new List<Color32>();

		[SerializeField] [HideInInspector]
		private float _scale = 1.0f;
		[SerializeField] [HideInInspector]
		private Color32 _color;
		[SerializeField] [HideInInspector]
		private Bool3 _mirrorAxes;

#pragma warning disable 0649
		private bool _isSculpting;
		private bool _isPainting;
		private bool _isDirty;

		private Mesh _cachedMesh;
		private float _cachedScale;
#pragma warning restore

		private bool TryAddIndex(Vector3Int index)
		{
			var added = _indices.AddUnique(index);
			if (added)
			{
				for (int d = 0; d < Axes.All3D.Length; d++)
				{
					_colors.Add(_color);
				}
			}
			return added;
		}

		private void RemoveIndex(Vector3Int index)
		{
			if (_indices.TryIndexOf(index, out int i))
			{
				_indices.RemoveAt(i);
				_colors.RemoveRange(i * Axes.All3D.Length, Axes.All3D.Length);
			}
		}

		private Mesh CreateNewCurrentMesh()
		{
			if (
				TryGetComponent(out MeshFilter meshFilter) &&
				TryGetComponent(out MeshCollider meshCollider)
			)
			{
				return meshCollider.sharedMesh = meshFilter.sharedMesh = new Mesh();
			}
			return null;
		}

		private void WriteToCurrentMesh()
		{
			var meshBuilder = new FlatMeshBuilder();

			for (int i = 0; i < _indices.Count; i++)
			{
				var index = _indices[i];

				for (int d = 0; d < Axes.All3D.Length; d++)
				{
					var direction = Axes.All3D[d];
					var neighbour = Vector3Int.RoundToInt(index + direction);

					if (!_indices.Contains(neighbour))
					{
						var c = i * Axes.All3D.Length + d;
						var color = _colors[c];

						var triangles = Cubes.Triangles[d];
						for (int t = 0; triangles[t] != -1; t += 3)
						{
							var v0 = (index + Cubes.Vertices[triangles[t + 0]]) * _scale;
							var v1 = (index + Cubes.Vertices[triangles[t + 1]]) * _scale;
							var v2 = (index + Cubes.Vertices[triangles[t + 2]]) * _scale;

							meshBuilder.AddTriangle(
								v0, v1, v2,
								color, color, color
							);
						}
					}
				}
			}

			meshBuilder.Overwrite(_cachedMesh);

			if (TryGetComponent<MeshCollider>(out var meshCollider))
			{	// Only because mesh has to be updated in physics cache
				meshCollider.sharedMesh = _cachedMesh;
			}

			_isDirty = true;
		}

		private void ReadFromCurrentMesh()
		{
			_indices.Clear();
			_colors.Clear();

			var bounds = Range3Int.Empty;
			var indicesNormals = new Dictionary<Vector3Int, HashSet<Vector3Int>>();

			var normals = _cachedMesh.normals;
			var vertices = _cachedMesh.vertices;
			var colors = _cachedMesh.colors32;

			const int kFaceVertexCount = 6;
			for (int v = 0; v < vertices.Length; v += kFaceVertexCount)
			{
				var v0 = vertices[v + 0];
				var v1 = vertices[v + 1];
				var v2 = vertices[v + 2];

				var minDistance = Vector3.Magnitude(v1 - v0);
				var maxDistance = Vector3.Magnitude(v0 - v2);

				_scale = Mathf.Min(_scale, minDistance);

				var normal = Vector3Int.RoundToInt(normals[v + 1]);
				var index = Vector3Int.RoundToInt((v2 + v0) * 0.5f / minDistance - Mathx.Mul(normal, 0.5f));
				TryAddIndex(index);

				if (Axes.All3D.TryIndexOf(normal, out int d))
				{
					var c = (_indices.Count - 1) * Axes.All3D.Length + d;
					_colors[c] = colors[v + 1];
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
							foreach (var axis in Axes.Positive3D)
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

		public bool CanSave()
		{
			return _isDirty;
		}

		public void Save()
		{
			var path = AssetDatabase.GetAssetPath(_cachedMesh);

			if (string.IsNullOrEmpty(path))
			{
				const string DEFAULT_NAME = "";
				const string DEFAULT_MESSAGE = "";
				path = EditorUtility.SaveFilePanelInProject("Save Mesh", DEFAULT_NAME, "asset", DEFAULT_MESSAGE);

				AssetDatabase.CreateAsset(_cachedMesh, path);
			}

			AssetDatabase.SaveAssets();

			_isDirty = false;
		}

		public bool IsSculpting()
		{
			return _isSculpting;
		}

		public string BeginSculpting()
		{
			if (_isSculpting)
				return "Couldn't begin sculpting. Already sculpting";

			_isSculpting = true;
			return null;
		}

		public string EndSculpting()
		{
			if (!_isSculpting)
				return "Couldn't end sculpting. Not sculpting";

			_isSculpting = false;
			return null;
		}

		public bool IsPainting()
		{
			return _isPainting;
		}

		public string BeginPainting()
		{
			if (_isPainting)
				return "Couldn't begin painting. Already painting";

			_isPainting = true;
			return null;
		}

		public string EndPainting()
		{
			if (!_isPainting)
				return "Couldn't end painting. Not painting";

			_isPainting = false;
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
					if (_isSculpting)
						DrawMirrorAxesGizmo();
					break;

				case EventType.MouseMove:
					HandleUtility.Repaint();
					break;

				case EventType.MouseDown:
					if (currentEvent.button == LEFT_MOUSE_BUTTON)
					{
						if (_isSculpting)
							Sculpt(currentEvent.mousePosition, currentEvent.shift);
						else if (_isPainting)
							Paint(currentEvent.mousePosition);
						currentEvent.Use();
					}
					break;

				case EventType.MouseDrag:
					if (currentEvent.button == LEFT_MOUSE_BUTTON)
					{
						if (_isPainting)
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
				var hitPoint = hit.point / _scale;
				var hitOffset = hit.transform.position / _scale;
				var hitNormal = hit.normal;

				var rectOffset = hitNormal * 0.5f - hitOffset;
				var rectPosition = (Mathx.Round(hitPoint + rectOffset) - rectOffset) * _scale;
				var rectRotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
				var rectScale = Vector3.one * _scale;
				var rect3D = Rects.GetVertices(rectPosition, rectScale, rectRotation);

				Handles.zTest = CompareFunction.Always;
				Handles.DrawSolidRectangleWithOutline(rect3D, kRectGizmoColor, kRectGizmoOutline);
			}
		}

		private void DrawMirrorAxesGizmo()
		{
			if (_mirrorAxes.Any())
			{
				var range = Range3Int.Empty;
				foreach (var index in _indices)
				{
					range.min = Mathx.Min(range.min, index);
					range.max = Mathx.Max(range.max, index);
				}

				var center = range.Center * _scale;
				var extents = Mathx.Mul((range.Extents + Vector3Int.one * 2), _scale);

				for (int i = 0; i < 3; ++i)
				{
					if (_mirrorAxes[i])
					{
						var selector = new Bool3(i == 0, i == 1, i == 2);
						var axisCenter = Mathx.Mul(center, Mathx.Select(Vector3.one, Vector3.zero, selector));
						var axisNormal = Mathx.Select(Vector3.zero, Vector3.one, selector);
						var rectPosition = axisCenter + this.transform.position;
						var rectSize = new Vector3(i == 0 ? extents.y : extents.x, 0.0f, i == 2 ? extents.y : extents.z);
						var rectRotation = Quaternion.FromToRotation(Vector3.up, axisNormal);
						var rect3D = Rects.GetVertices(rectPosition, rectSize, rectRotation);

						Handles.zTest = CompareFunction.Less;
						Handles.DrawSolidRectangleWithOutline(rect3D, new Color(axisNormal.x, axisNormal.y, axisNormal.z, 32.0f / 255.0f), kRectGizmoOutline);
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

				var hitPoint = hit.transform.InverseTransformPoint(hit.point) / _scale;
				var hitNormal = hit.normal;
				var hitOffset = hitNormal * 0.5f;

				if (remove)
				{
					var index = Vector3Int.RoundToInt(hitPoint - hitOffset);

					RemoveIndex(index);

					void TryRemoveMirrorVoxel()
					{
						if (_mirrorAxes.x && _mirrorAxes.y && _mirrorAxes.z)
						{
							var mirror = index;
							mirror *= -1;

							RemoveIndex(mirror);
						}
					}

					void TryRemoveMirrorVoxelByAxis(int i)
					{
						if (_mirrorAxes[i] && index[i] != 0)
						{
							var mirror = index;
							mirror[i] *= -1;

							RemoveIndex(mirror);
						}
					}

					void TryRemoveMirrorVoxelByAxes(int a, int b)
					{
						if (_mirrorAxes[a] && _mirrorAxes[b] && (index[a] != 0 || index[b] != 0))
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
					var index = Vector3Int.RoundToInt(hitPoint + hitOffset);

					TryAddIndex(index);

					void TryCreateMirrorVoxel()
					{
						if (_mirrorAxes.All())
						{
							var mirror = index;
							mirror *= -1;

							TryAddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxis(int i)
					{
						if (_mirrorAxes[i] && index[i] != 0)
						{
							var mirror = index;
							mirror[i] *= -1;

							TryAddIndex(mirror);
						}
					}

					void TryCreateMirrorVoxelByAxes(int a, int b)
					{
						if (_mirrorAxes[a] && _mirrorAxes[b] && (index[a] != 0 || index[b] != 0))
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
				var hitPoint = hit.transform.InverseTransformPoint(hit.point) / _scale;
				var hitNormal = hit.normal;
				var hitOffset = hitNormal * 0.5f;

				var index = Vector3Int.RoundToInt(hitPoint - hitOffset);
				var direction = Vector3Int.RoundToInt(hitNormal);

				if (
					_indices.TryIndexOf(index, out int i) &&
					Axes.All3D.TryIndexOf(direction, out int d)
				)
				{
					var c = i * Axes.All3D.Length + d;
					var currentColor = _colors[c];

					if (Utility.TryUpdate(ref currentColor, _color))
					{
						_colors[c] = currentColor;

						WriteToCurrentMesh();
					}
				}
			}
		}

		public string Flip(bool x = false, bool y = false, bool z = false)
		{
			var selector = new Bool3(x, y, z);
			var flipper = Mathx.Select(Vector3Int.one, -Vector3Int.one, selector);

			for (int i = 0; i < _indices.Count; i++)
				_indices[i] *= flipper;

			WriteToCurrentMesh();

			return null;
		}

		private void Update()
		{
			if (
				TryGetComponent(out MeshFilter meshFilter) &&
				TryGetComponent(out MeshCollider meshCollider)
			)
			{
				if (Utility.TryUpdate(ref _cachedMesh, meshFilter.sharedMesh))
				{
					if (_cachedMesh == null)
					{
						_cachedMesh = CreateNewCurrentMesh();
						WriteToCurrentMesh();
					}
					else
					{
						ReadFromCurrentMesh();
						meshCollider.sharedMesh = _cachedMesh;
					}
				}

				if (Utility.TryUpdate(ref _cachedScale, _scale))
				{
					WriteToCurrentMesh();
				}
			}
		}

		private void Reset()
		{
			_scale = 1.0f;
			_color = Color.white;

			_indices.Clear();
			_colors.Clear();

			_mirrorAxes = Bool3.False;

			TryAddIndex(Vector3Int.zero);

			_cachedMesh = CreateNewCurrentMesh();
			WriteToCurrentMesh();
		}
	}
#else
	public class VoxelSculptor : MonoBehaviour
	{
	}
#endif
}