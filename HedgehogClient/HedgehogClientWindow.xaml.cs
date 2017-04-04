﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;

namespace HedgehogClient {
    /// <summary>
    /// Interaction logic for HedgehogClientWindow.xaml
    /// </summary>
    public partial class HedgehogClientWindow : Window {
        private readonly IPAddress _address;
        private TcpClient _client;
        private SocketStatus _status;
        private ControlKeys.MovementKey _currentKey = ControlKeys.MovementKey.Stop;

        private static TaskCompletionSource<bool> _tcs;

        //Port for Hedgehog Server
        private const int Port = 3131;
        //Timeout in Milliseconds for Sends
        private const int SendTimeout = 3000;

        private enum SocketStatus {
            Disconnected,
            Connecting,
            Connected,
            Busy
        }

        public HedgehogClientWindow(IPAddress address) {
            InitializeComponent();
            _address = address;
            ipLabel.Content = _address + ":" + Port;

            Connect();
        }

        //Set Green or Red Hedgehog Icon
        private void SetHedgehogIcon(bool green) {
            try {
                if(green) {
                    Bitmap bitmap = Properties.Resources.Hedgehog_Green.ToBitmap();
                    IntPtr hBitmap = bitmap.GetHbitmap();
                    ImageSource source =
                        Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero, Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    statusImage.Source = source;
                    Icon = source;
                } else {
                    Bitmap bitmap = Properties.Resources.Hedgehog_Red.ToBitmap();
                    IntPtr hBitmap = bitmap.GetHbitmap();
                    ImageSource source =
                        Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero, Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    statusImage.Source = source;
                    Icon = source;
                }
            } catch {
                //ignored
            }
        }

        //Disconnected Callback
        private void Disconnected(bool byUser, string message = null) {
            DisconnectButton.IsEnabled = false;
            DisconnectButton.ToolTip = "Already disconnected";
            _status = SocketStatus.Disconnected;
            statusLabel.Content = "Disconnected";
            statusLabel.Foreground = Brushes.Red;
            SetHedgehogIcon(false);

            string msgBoxText = "The connection to the Hedgehog has been lost!";
            if(message != null) {
                msgBoxText += "\n\r" + message;
            }

            if(!byUser)
                MessageBox.Show(msgBoxText, "Disconnected", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        //Log to Console
        private void Log(string message) {
            logBox.Text += $"({DateTime.Now:HH:mm:ss}) > " + message + Environment.NewLine;
            logBox.ScrollToLine(logBox.LineCount - 2);
        }

        #region Socket
        //Connect the Server and give User Feedback
        private async void Connect() {
            Log("Connecting to Hedgehog...");
            Cursor = Cursors.AppStarting;
            statusLabel.Content = "Connecting...";
            statusLabel.Foreground = Brushes.Orange;

            _client = new TcpClient();

            try {
                _status = SocketStatus.Connecting;

                await _client.ConnectAsync(_address, Port);

                if(_client.Connected) {
                    byte[] buffer = new byte[1];
                    _client.Client.BeginReceive(buffer, 0, 1, SocketFlags.None, Received, null);

                    Log("Connected!");
                    _status = SocketStatus.Connected;
                    statusLabel.Content = "Connected";
                    statusLabel.Foreground = Brushes.Green;
                    SetHedgehogIcon(true);
                } else {
                    Log("ERROR: Error Connecting!");
                    _status = SocketStatus.Disconnected;
                    statusLabel.Content = "Disconnected";
                    statusLabel.Foreground = Brushes.Red;
                    SetHedgehogIcon(false);
                }
            } catch(Exception e) {
                Log("ERROR: Error Connecting!");
                Log("ERROR: " + e.Message);
                DisconnectButton.IsEnabled = false;
                DisconnectButton.ToolTip = "Not yet connected";
                _status = SocketStatus.Disconnected;
                statusLabel.Content = "Disconnected";
                statusLabel.Foreground = Brushes.Red;
                SetHedgehogIcon(false);
                MessageBox.Show($"Could not Connect!\n\r{e.Message}", "Error Connecting to Hedgehog!", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Cursor = Cursors.Arrow;
        }

        //Send Key (Integer from 0 to 7) to Hedgehog
        private async Task SendKey(ControlKeys.MovementKey movementKey) {
            int i = 0;

            while(_status == SocketStatus.Busy) {
                await Task.Delay(10);
                i += 10;

                if(i >= SendTimeout) {
                    Log("ERROR: Could not Send Message, Hedgehog Send Request Timed out!");
                    throw new HedgehogException("Hedgehog Send Request Timed out!");
                }
            }

            if(_status == SocketStatus.Disconnected || _status == SocketStatus.Connecting) {
                throw new HedgehogException("Not connected to Hedgehog!");
            }

            _status = SocketStatus.Busy;

            byte key = (byte)movementKey;
            byte[] message = { key };

            _currentKey = movementKey;
            _client.Client.BeginSend(message, 0, 1, SocketFlags.None, Sent, null);
        }

        //Mesage Sent Callback
        private void Sent(IAsyncResult result) {
            _client.Client.EndSend(result);

            if(result.IsCompleted) {
                _tcs?.SetResult(true);
            } else {
                //Invoke to main Thread
                Dispatcher.BeginInvoke(new Action(delegate {
                    Disconnected(false, "Tried to send Message to Hedgehog, failed");
                }));
                _tcs?.SetResult(false);
            }

            _status = SocketStatus.Connected;
        }

        //Message Received Callback       | On Receive from Server => Socket closed
        private void Received(IAsyncResult result) {
            _client.Client.EndReceive(result);
            Disconnected(false, "Server shut down connection!");
        }

        //Disconnect the Client
        private async void Disconnect(bool byUser, string message = null) {
            while(_status == SocketStatus.Busy) {
                await Task.Delay(10);
            }

            if(_status == SocketStatus.Connected) {
                _client.Close();
                await Dispatcher.BeginInvoke(new Action(delegate {
                    Disconnected(byUser, message);
                }));
            }
        }
        #endregion

        #region Action Listeners
        //KeyUp Unlocks a Key (e.g. W)
        private async void WindowKeyUp(object sender, KeyEventArgs e) {
            try {
                _currentKey = ControlKeys.MovementKey.Stop;
                await SendKey(ControlKeys.MovementKey.Stop);
            } catch {
                // ignored
            }

            keyLabel.Content = "/";
        }

        //KeyDown Locks a Key (e.g. W) and drives forward till KeyUp)
        private async void WindowKeyDown(object sender, KeyEventArgs e) {
            try {
                if(_currentKey != ControlKeys.MovementKey.Stop)
                    return;

                keyLabel.Content = "Sending...";

                Key key = e.Key;
                ControlKeys.MovementKey movementKey = ControlKeys.GetKey(key);
                _currentKey = movementKey;

                //DEBUG
                _status = SocketStatus.Busy;


                _tcs = new TaskCompletionSource<bool>();
                await SendKey(ControlKeys.MovementKey.Stop);
                await _tcs.Task;

                _tcs = new TaskCompletionSource<bool>();
                await SendKey(movementKey);
                await _tcs.Task;

                keyLabel.Content = ControlKeys.FriendlyStatus(movementKey);
            } catch {
                keyLabel.Content = "/";
                //Wrong input
            }
        }

        //Disconnect Button
        private void DisconnectClick(object sender, RoutedEventArgs e) {
            Disconnect(true, "Disconnected by User.");
        }

        //Close Window
        private void WindowClosing(object sender, CancelEventArgs e) {
            Disconnect(true, "Window closed");

            new MainWindow().Show();
        }
        #endregion
    }
}
