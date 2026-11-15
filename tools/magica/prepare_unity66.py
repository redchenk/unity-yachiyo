"""Apply local Unity 6.6 compatibility edits to the user's licensed MC2 2.15 package.
Does not contain or download the proprietary package. Re-running is safe.
"""
from pathlib import Path
root=Path(__file__).resolve().parents[2]/'UnityProject/Assets/MagicaCloth2'
if not root.exists():raise SystemExit('Import your licensed Magica Cloth 2 first')
for p in (root/'Scripts').rglob('*.cs'):
 s=p.read_text(encoding='utf-8-sig')
 s=s.replace('AssetDatabase.Contains(cloth.gameObject.GetInstanceID())','AssetDatabase.Contains(cloth.gameObject)')
 s=s.replace('Selection.Contains(component.gameObject.GetInstanceID())','Selection.Contains(component.gameObject)')
 s=s.replace('EditorApplication.hierarchyWindowItemOnGUI','EditorApplication.hierarchyWindowItemByEntityIdOnGUI')
 s=s.replace('DrawIcon(int instanceId, Rect rect)','DrawIcon(EntityId instanceId, Rect rect)')
 s=s.replace('EditorUtility.InstanceIDToObject(instanceId)','EditorUtility.EntityIdToObject(instanceId)')
 s=s.replace('.GetInstanceID()', '.MagicaObjectId()').replace('? GetInstanceID() :','? this.MagicaObjectId() :')
 if s!=p.read_text(encoding='utf-8-sig'):p.write_text(s)
# Stable, collision-free int handles for MC2's internal native buffers. Never truncate
# or hash a 64-bit EntityId; editor APIs above receive real objects / EntityIds.
(root/'Scripts/Core/Utility/YachiyoUnity66ObjectIds.cs').write_text('''using System.Collections.Generic;
using UnityEngine;
namespace MagicaCloth2 {
 public static class YachiyoUnity66ObjectIds {
  static readonly Dictionary<EntityId,int> ids = new Dictionary<EntityId,int>();
  public static int MagicaObjectId(this Object value) {
   if (!value) return 0;
   var entity = value.GetEntityId();
   lock(ids) { if (!ids.TryGetValue(entity,out int id)) { id=checked(ids.Count+1); ids.Add(entity,id); } return id; }
  }
 }
}
''')
print('Applied local MC2 Unity 6.6 compatibility')
# MC2's nearest-proxy mapping may classify originally fixed / excluded render
# vertices as movable near a reduced boundary. Honor explicit runtime paint.
p=root/'Scripts/Core/Cloth/ClothProcess.cs'
s=p.read_text();needle='vmesh.Mapping(proxyMesh);'
patch='''vmesh.Mapping(proxyMesh);
                                // Yachiyo: keep explicit nonmoving render vertices on original skinning.
                                int sourceIndex = renderMeshInfos.IndexOf(info);
                                if (sdata2.vertexAttributeList != null && sourceIndex < sdata2.vertexAttributeList.Count)
                                {
                                    var painted = sdata2.vertexAttributeList[sourceIndex];
                                    for (int vertex = 0; vertex < vmesh.VertexCount; vertex++)
                                    {
                                        int original = vmesh.referenceIndices[vertex];
                                        if (original >= 0 && original < painted.Length && !painted[original].IsMove())
                                            vmesh.attributes[vertex] = painted[original];
                                    }
                                }'''
if 'Yachiyo: keep explicit nonmoving' not in s:
 if s.count(needle)!=1:raise SystemExit('Unexpected MC2 version: mapping patch requires review')
 p.write_text(s.replace(needle,patch))
print('Preserved explicit fixed/excluded vertex paint during reduced proxy mapping')
