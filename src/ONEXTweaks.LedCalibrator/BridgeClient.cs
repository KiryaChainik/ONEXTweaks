using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ONEXTweaks.Calibration
{
    internal sealed class BridgeClient : IDisposable
    {
        private readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=262144,RecursionLimit=30};
        private NamedPipeClientStream pipe;private StreamReader reader;private StreamWriter writer;
        private TaskCompletionSource<Dictionary<string,object>> pending;private int requestId=76000;private bool disposed;
        public async Task ConnectAsync()
        {
            pipe=new NamedPipeClientStream(".","ONEXTweaks.Widget.v1",PipeDirection.InOut,PipeOptions.Asynchronous);
            await Task.Run(()=>pipe.Connect(10000));reader=new StreamReader(pipe);writer=new StreamWriter(pipe,new UTF8Encoding(false)){AutoFlush=true};
            ReadReplies();
        }
        private async void ReadReplies()
        {
            try{
                while(!disposed){string line=await reader.ReadLineAsync();if(line==null)throw new IOException("Подключение к помощнику закрыто");
                    Dictionary<string,object> reply;lock(json)reply=json.Deserialize<Dictionary<string,object>>(line);object id;
                    if(reply!=null&&reply.TryGetValue("RequestId",out id)&&Convert.ToInt32(id)==requestId&&pending!=null)pending.TrySetResult(reply);
                }
            }catch(Exception error){if(!disposed&&pending!=null)pending.TrySetException(error);}
        }
        public async Task<Dictionary<string,object>> SendAsync(Dictionary<string,object> body)
        {
            if(disposed||pipe==null)throw new IOException("Нет подключения к помощнику");
            if(pending!=null)throw new InvalidOperationException("Предыдущая команда ещё выполняется");
            var completion=new TaskCompletionSource<Dictionary<string,object>>();pending=completion;body["RequestId"]=++requestId;
            try{
                string packet;lock(json)packet=json.Serialize(body);await writer.WriteLineAsync(packet);await writer.FlushAsync();
                if(await Task.WhenAny(completion.Task,Task.Delay(15000))!=completion.Task){Dispose();throw new IOException("Помощник не подтвердил команду за 15 секунд");}
                var reply=await completion.Task;object error;if(reply.TryGetValue("Error",out error))throw new IOException(Convert.ToString(error));return reply;
            }finally{pending=null;}
        }
        public void Dispose(){disposed=true;if(pipe!=null)pipe.Dispose();}
    }
}
