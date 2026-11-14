using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Yachiyo {
 // A private, owned WebView2 child window. Messages never leave the machine.
 public static class UnifiedWindowsWebView {
  static NamedPipeServerStream pipe;static StreamWriter writer;static Process host;
  static readonly object gate=new object();
  static string layout="{}",result="{}",error="";static float dx,dy,zoom,pan;
  public static string Layout {get{lock(gate)return layout;}}
  public static string Result {get{lock(gate)return result;}}
  public static string Error {get{lock(gate)return error;}}
  public static void Open(string root,string url,bool verify){
   string name="yachiyo-web-"+Guid.NewGuid().ToString("N");
   pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
   var thread=new Thread(()=>ReadMessages()){IsBackground=true};thread.Start();
   var start=new ProcessStartInfo(Path.Combine(root,"native","YachiyoWebHost.exe"),"--parent "+Process.GetCurrentProcess().Id+" --pipe "+name+" --url \""+url+"\""+(verify?" --verify":"")){UseShellExecute=false,CreateNoWindow=true};
   host=Process.Start(start);
  }
  static void ReadMessages(){
   try{
    pipe.WaitForConnection();lock(gate){writer=new StreamWriter(pipe,new UTF8Encoding(false)){AutoFlush=true};}
    var reader=new StreamReader(pipe,Encoding.UTF8);string line;
    while((line=reader.ReadLine())!=null){int split=line.IndexOf('\t');if(split<0)continue;var kind=line.Substring(0,split);var data=line.Substring(split+1);
     lock(gate){if(kind=="layout")layout=data;else if(kind=="result")result=data;else if(kind=="error")error=data;else if(kind=="camera"){
      var values=data.Split(',');if(values.Length==4){var c=System.Globalization.CultureInfo.InvariantCulture;dx+=float.Parse(values[0],c);dy+=float.Parse(values[1],c);zoom+=float.Parse(values[2],c);pan=float.Parse(values[3],c);}
     }}
    }
   }catch(ObjectDisposedException){}catch(Exception e){lock(gate)error="Windows 界面连接中断："+e.Message;}
  }
  public static void CameraInput(float[] values){lock(gate){values[0]=dx;values[1]=dy;values[2]=zoom;values[3]=pan;dx=dy=zoom=0;}}
  public static void Evaluate(string script){lock(gate){try{writer?.WriteLine("eval\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(script)));}catch(IOException){}}}
  public static void Close(){lock(gate){try{writer?.WriteLine("close\t");}catch{}writer=null;}try{pipe?.Dispose();}catch{} }
 }
}
