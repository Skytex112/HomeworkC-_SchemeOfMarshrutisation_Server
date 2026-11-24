using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeworkC__SchemeOfMarshrutisation
{
 // Simple placeholder class representing connected client metadata
 public class ClientInfo
 {
 public string DisplayName { get; set; }
 public string Subscription { get; set; }
 public bool IsConnected { get; set; }

 // Networking fields
 public TcpClient TcpClient { get; set; }
 public NetworkStream Stream { get; set; }

 public ClientInfo()
 {
 DisplayName = "Unknown";
 Subscription = "General";
 IsConnected = true;
 TcpClient = null;
 Stream = null;
 }

 public async Task SendAsync(string text)
 {
 if (Stream == null || !TcpClient.Connected) return;
 try
 {
 var bytes = Encoding.UTF8.GetBytes(text);
 await Stream.WriteAsync(bytes,0, bytes.Length);
 }
 catch
 {
 // swallowing exception here; caller will handle removal if needed
 }
 }
 }
}
