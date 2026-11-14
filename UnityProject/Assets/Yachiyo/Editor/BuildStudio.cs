using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

namespace Yachiyo.Editor {
 public static class BuildStudio {
  const string Model="Assets/#八千代/#OkaRuto_月见一万代_FT.fbx";
  const string Scene="Assets/Yachiyo/Studio.unity";
  [MenuItem("Yachiyo/Create Studio")]
  public static void Create(){
   var shader=Shader.Find("Yachiyo/SoftToon");if(!shader)throw new Exception("Shader missing");
   foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/#八千代"})){
    var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
    var so=new SerializedObject(mat);var saved=so.FindProperty("m_SavedProperties.m_TexEnvs");Texture tex=null;
    for(int i=0;i<saved.arraySize;i++){var e=saved.GetArrayElementAtIndex(i);if(e.FindPropertyRelative("first").stringValue=="_MainTex")tex=e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;}
    mat.shader=shader;mat.SetTexture("_MainTex",tex);mat.SetColor("_Color",Color.white);mat.SetFloat("_Cutoff",.25f);EditorUtility.SetDirty(mat);
   }
   var importer=(ModelImporter)AssetImporter.GetAtPath(Model);importer.isReadable=true;importer.importBlendShapes=true;importer.animationType=ModelImporterAnimationType.Human;importer.SaveAndReimport();
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   RenderSettings.ambientLight=new Color(.7f,.75f,.85f);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/#八千代/YachiyoRuntime.prefab");
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
   var model=UnityEngine.Object.Instantiate(prefab?prefab:source);model.name="Yachiyo";model.transform.position=Vector3.zero;
   var motion=model.AddComponent<AvatarMotion>();motion.animator=model.GetComponent<Animator>();
   motion.animator.avatar=source.GetComponent<Animator>().avatar;motion.animator.runtimeAnimatorController=null;
   if(!motion.animator.avatar.isValid||!motion.animator.avatar.isHuman)throw new Exception("Avatar import invalid");
   ClothSetup.Configure(motion);
   var light=new GameObject("Soft key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(30,155,0);
   FaceSetup.Configure(motion);
   var cam=new GameObject("Main Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.055f,.075f,.095f);cam.fieldOfView=36;cam.nearClipPlane=.05f;cam.farClipPlane=40;cam.transform.position=new Vector3(0,1.28f,3.1f);cam.transform.LookAt(new Vector3(0,.85f,0));cam.gameObject.AddComponent<AudioListener>();
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cylinder);floor.name="Stage";floor.transform.position=new Vector3(0,-.045f,0);floor.transform.localScale=new Vector3(2.3f,.04f,2.3f);
   var floorMat=new Material(Shader.Find("Unlit/Color")){color=new Color(.095f,.135f,.16f)};AssetDatabase.CreateAsset(floorMat,"Assets/Yachiyo/Stage.mat");floor.GetComponent<Renderer>().sharedMaterial=floorMat;
   var ring=new GameObject("Stage ring").AddComponent<LineRenderer>();ring.positionCount=97;ring.widthMultiplier=.008f;var ringMat=new Material(Shader.Find("Unlit/Color")){color=new Color(.23f,.52f,.47f)};AssetDatabase.CreateAsset(ringMat,"Assets/Yachiyo/Ring.mat");ring.sharedMaterial=ringMat;
   for(int i=0;i<97;i++){float a=i/96f*Mathf.PI*2;ring.SetPosition(i,new Vector3(Mathf.Cos(a)*1.02f,.002f,Mathf.Sin(a)*1.02f));}
   var ui=new GameObject("Studio UI").AddComponent<StudioUI>();ui.avatar=motion;ui.view=cam;
   PlayerSettings.companyName="Tsukuyomi";PlayerSettings.productName="Yachiyo Motion Studio";PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=800;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
   QualitySettings.vSyncCount=1;
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Scene,true)};AssetDatabase.SaveAssets();
   Report(model);motion.Initialize();motion.Tick(.1f);Capture(cam,"../artifacts/avatar-preview.png");
  }
  static void Report(GameObject model){
   var a=model.GetComponent<Animator>();var report=new AvatarReport{human=a.avatar.isHuman,valid=a.avatar.isValid,bones=new List<string>(),meshes=new List<MeshReport>()};
   for(int i=0;i<(int)HumanBodyBones.LastBone;i++){var b=(HumanBodyBones)i;var t=a.GetBoneTransform(b);if(t)report.bones.Add(b+":"+t.name);}
   foreach(var m in model.GetComponentsInChildren<SkinnedMeshRenderer>()){var names=new List<string>();for(int i=0;i<m.sharedMesh.blendShapeCount;i++)names.Add(m.sharedMesh.GetBlendShapeName(i));report.meshes.Add(new MeshReport{name=m.name,vertices=m.sharedMesh.vertexCount,shapes=names});}
   File.WriteAllText("../artifacts/avatar-report.json",JsonUtility.ToJson(report,true));
  }
  static void Capture(Camera cam,string path){var rt=new RenderTexture(1280,1000,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);}
  public static void ValidateSample(){EditorSceneManager.OpenScene(Scene);var avatar=UnityEngine.Object.FindFirstObjectByType<AvatarMotion>();avatar.Initialize();var clip=JsonUtility.FromJson<MotionClip>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Motions/kimodo-official.json")));avatar.Play(clip,"Test");avatar.Tick(1.3f);Capture(Camera.main,"../artifacts/kimodo-preview.png");Debug.Log("MOTION_VALIDATION_PASS "+clip.frames.Length+" frames");}
  public static void BuildMac(){EditorSceneManager.OpenScene(Scene);Directory.CreateDirectory("../build");var result=BuildPipeline.BuildPlayer(new[]{Scene},"../build/Yachiyo Motion Studio.app",BuildTarget.StandaloneOSX,BuildOptions.CleanBuildCache);if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed "+result.summary.result);}
  public static void BuildWindows(){
   EditorSceneManager.OpenScene(Scene);Directory.CreateDirectory("../build/Windows");
   int width=PlayerSettings.defaultScreenWidth,height=PlayerSettings.defaultScreenHeight;bool resize=PlayerSettings.resizableWindow;var mode=PlayerSettings.fullScreenMode;
   try{
    PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;PlayerSettings.resizableWindow=false;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
    var result=BuildPipeline.BuildPlayer(new[]{Scene},"../build/Windows/Yachiyo.exe",BuildTarget.StandaloneWindows64,BuildOptions.CleanBuildCache);if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed "+result.summary.result);
   }finally{PlayerSettings.defaultScreenWidth=width;PlayerSettings.defaultScreenHeight=height;PlayerSettings.resizableWindow=resize;PlayerSettings.fullScreenMode=mode;}
  }
  [Serializable]class AvatarReport{public bool human,valid;public List<string>bones;public List<MeshReport>meshes;}
  [Serializable]class MeshReport{public string name;public int vertices;public List<string>shapes;}
 }
}
