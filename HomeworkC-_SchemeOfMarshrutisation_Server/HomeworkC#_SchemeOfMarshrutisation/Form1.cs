using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace HomeworkC__SchemeOfMarshrutisation
{
    public partial class ServerForm : Form
    {

        TcpListener listener;
        List<ClientInfo> clients = new List<ClientInfo>();
        readonly object clientsLock = new object();
        CancellationTokenSource acceptCts;

        public ServerForm()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // UI initialization if needed
        }

        // Event handler stubs - implement logic yourself
        private void btnStartServer_Click(object sender, EventArgs e)
        {
            AddLog("Start server clicked");
            int port =5000;
            int.TryParse(txtPort.Text, out port);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            acceptCts = new CancellationTokenSource();
            _ = AcceptClientsAsync(acceptCts.Token);
            AddLog($"Listening on port {port}");
        }

        private void btnStopServer_Click(object sender, EventArgs e)
        {
            AddLog("Stop server clicked");
            try
            {
                acceptCts?.Cancel();
                listener?.Stop();
            }
            catch (Exception ex)
            {
                AddLog($"Error stopping listener: {ex.Message}");
            }

            // Disconnect clients
            lock (clientsLock)
            {
                foreach (var c in clients)
                {
                    try
                    {
                        c.Stream?.Close();
                        c.TcpClient?.Close();
                        c.IsConnected = false;
                    }
                    catch { }
                }
                clients.Clear();
            }

            RefreshClientList();
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            var message = txtMessage.Text ?? string.Empty;
            var type = cmbMessageType.SelectedItem != null ? cmbMessageType.SelectedItem.ToString() : "General";
            var emergency = chkEmergency.Checked;
            AddLog($"Send clicked. Type={type}, Emergency={emergency}, MessageLength={message.Length}");

            string payload = $"MESSAGE|{(emergency ? "EMERGENCY" : type)}|{message}";
            await BroadcastAsync(payload, emergency, type);
        }

        async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient tcpClient = null;
                try
                {
                    tcpClient = await listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Listener stopped
                    break;
                }
                catch (Exception ex)
                {
                    AddLog($"Accept error: {ex.Message}");
                    break;
                }

                _ = HandleClientAsync(tcpClient);
            }
        }

        async Task HandleClientAsync(TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();
            var clientInfo = new ClientInfo() { TcpClient = tcpClient, Stream = stream };

            // Expect registration message first: REGISTER|DisplayName|Subscription
            try
            {
                var reg = await ReadMessageAsync(stream);
                if (!string.IsNullOrEmpty(reg) && reg.StartsWith("REGISTER|"))
                {
                    var parts = reg.Split(new[] { '|' },3);
                    if (parts.Length >=3)
                    {
                        clientInfo.DisplayName = parts[1];
                        clientInfo.Subscription = parts[2];
                    }
                }
                else
                {
                    // if no proper registration, set defaults
                    clientInfo.DisplayName = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    clientInfo.Subscription = "General";
                }

                clientInfo.IsConnected = true;

                lock (clientsLock)
                {
                    clients.Add(clientInfo);
                }

                AddLog($"Client connected: {clientInfo.DisplayName} (sub: {clientInfo.Subscription})");
                RefreshClientList();

                // send acknowledgement
                await clientInfo.SendAsync("REGISTERED");

                // Start listening to this client for any messages (optional)
                await ReceiveLoopAsync(clientInfo);
            }
            catch (Exception ex)
            {
                AddLog($"Client handling error: {ex.Message}");
                try { stream.Close(); tcpClient.Close(); } catch { }
            }
            finally
            {
                // ensure removal
                RemoveClient(clientInfo);
            }
        }

        async Task<string> ReadMessageAsync(NetworkStream stream)
        {
            // Simple single-read implementation; expects the whole message in one chunk
            var buffer = new byte[4096];
            using (var ms = new MemoryStream())
            {
                int bytesRead =0;
                // Read with short timeout to try to get the full message
                stream.ReadTimeout =5000;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer,0, buffer.Length);
                }
                catch (IOException)
                {
                    return null;
                }

                if (bytesRead <=0) return null;
                ms.Write(buffer,0, bytesRead);
                return Encoding.UTF8.GetString(ms.ToArray(),0, (int)ms.Length);
            }
        }

        async Task ReceiveLoopAsync(ClientInfo client)
        {
            var stream = client.Stream;
            var tcp = client.TcpClient;
            var buffer = new byte[4096];

            try
            {
                while (tcp.Connected)
                {
                    int bytesRead =0;
                    try
                    {
                        bytesRead = await stream.ReadAsync(buffer,0, buffer.Length);
                    }
                    catch
                    {
                        break;
                    }

                    if (bytesRead ==0) break; // disconnected

                    var msg = Encoding.UTF8.GetString(buffer,0, bytesRead);
                    AddLog($"Received from {client.DisplayName}: {msg}");

                    // Here we could handle client messages (e.g., change subscription) if desired.
                }
            }
            catch (Exception ex)
            {
                AddLog($"ReceiveLoop error for {client.DisplayName}: {ex.Message}");
            }
            finally
            {
                client.IsConnected = false;
                try { client.Stream.Close(); client.TcpClient.Close(); } catch { }
                RemoveClient(client);
            }
        }

        async Task BroadcastAsync(string payload, bool emergency, string type)
        {
            List<ClientInfo> snapshot;
            lock (clientsLock)
            {
                snapshot = clients.ToList();
            }

            foreach (var c in snapshot)
            {
                if (!c.IsConnected) continue;
                if (emergency || string.Equals(c.Subscription, type, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await c.SendAsync(payload);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Error sending to {c.DisplayName}: {ex.Message}");
                    }
                }
            }
        }

        void RemoveClient(ClientInfo client)
        {
            bool removed = false;
            lock (clientsLock)
            {
                removed = clients.Remove(client);
            }

            if (removed)
            {
                AddLog($"Client disconnected: {client.DisplayName}");
                RefreshClientList();
            }
        }

        // Simple helper to add a line to the log textbox
        private void AddLog(string text)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AddLog(text)));
                return;
            }

            var ts = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{ts}] {text}{Environment.NewLine}");
        }

        // Helper to update clients listbox (no networking logic here)
        private void RefreshClientList()
        {
            if (lstClients.InvokeRequired)
            {
                lstClients.Invoke(new Action(RefreshClientList));
                return;
            }

            lstClients.Items.Clear();
            lock (clientsLock)
            {
                foreach (var c in clients)
                {
                    lstClients.Items.Add($"{c.DisplayName} ({c.Subscription})");
                }
            }
        }
    }
}
