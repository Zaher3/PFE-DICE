using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

/// <summary>
/// Manages MQTT communication between Unity and an MQTT broker.
/// Thread-safe implementation that avoids coroutines for connection handling.
/// </summary>
public class MQTTCommunicationManager : MonoBehaviour
{
    [Header("Broker Configuration")]
    [SerializeField]
    [Tooltip("MQTT broker address (IP or hostname)")]
    private string brokerAddress = "localhost";

    [SerializeField]
    [Tooltip("MQTT broker port (default: 1883)")]
    private int brokerPort = 1883;

    [SerializeField]
    [Tooltip("Client ID for this Unity application")]
    private string clientId = "UnityMRApp";

    [SerializeField]
    [Tooltip("Username for broker authentication (if required)")]
    private string username = "";

    [SerializeField]
    [Tooltip("Password for broker authentication (if required)")]
    private string password = "";

    [Header("Connection Settings")]
    [SerializeField]
    [Tooltip("Auto-connect on start")]
    private bool connectOnStart = true;

    [SerializeField]
    [Tooltip("Initial reconnection interval in seconds")]
    private float initialReconnectInterval = 1.0f;

    [SerializeField]
    [Tooltip("Maximum reconnection interval in seconds")]
    private float maxReconnectInterval = 60.0f;

    [SerializeField]
    [Tooltip("Max number of reconnection attempts (0 = infinite)")]
    private int maxReconnectAttempts = 5;

    [Header("Diagnostic Settings")]
    [SerializeField]
    [Tooltip("Debug mode - logs all MQTT events to console")]
    private bool debugMode = true;

    [SerializeField]
    [Tooltip("Verbose mode - logs detailed connection and message info")]
    private bool verboseLogging = true;

    [SerializeField]
    [Tooltip("Log received message content (may be large)")]
    private bool logMessageContent = true;

    // Private state variables
    private bool m_IsConnected = false;
    private bool m_IsConnecting = false;
    private int m_ReconnectAttempts = 0;
    private float m_CurrentReconnectInterval;
    private string m_LastConnectionError = "";
    private DateTime m_LastConnectionAttempt;
    private string m_ClientIdWithSuffix = "";
    private float m_NextReconnectTime = 0f;
    private float m_NextPingTime = 0f;
    private object m_LockObject = new object();

    // MQTT client
    private MqttClient mqttClient;

    // Dictionary to store topic subscription callbacks
    private Dictionary<string, Action<string>> topicCallbacks =
        new Dictionary<string, Action<string>>();

    // List to store pending subscriptions when client isn't connected yet
    private List<KeyValuePair<string, Action<string>>> pendingSubscriptions =
        new List<KeyValuePair<string, Action<string>>>();

    // Queue for messages to be processed on the main thread
    private readonly Queue<MqttMessage> _messageQueue = new Queue<MqttMessage>();

    // Simple struct to hold MQTT messages
    private struct MqttMessage
    {
        public string Topic;
        public string Payload;
    }

    // Events
    public event Action onConnected;
    public event Action<string> onConnectionFailed;
    public event Action onConnectionLost;
    public event Action<string, string> onMessageReceived;

    // Properties
    public bool IsConnected
    {
        get
        {
            lock (m_LockObject)
            {
                return m_IsConnected && mqttClient != null && mqttClient.IsConnected;
            }
        }
    }
    public bool IsConnecting
    {
        get
        {
            lock (m_LockObject)
            {
                return m_IsConnecting;
            }
        }
    }
    public string BrokerAddress => brokerAddress;
    public int BrokerPort => brokerPort;
    public int ReconnectAttempts => m_ReconnectAttempts;
    public string LastConnectionError => m_LastConnectionError;
    public string ClientId => m_ClientIdWithSuffix;
    public DateTime LastConnectionAttempt => m_LastConnectionAttempt;

    // Thread-safe queue for main thread execution
    private readonly Queue<Action> _mainThreadActions = new Queue<Action>();
    private readonly object _mainThreadActionsLock = new object();

    void Awake()
    {
        LogMessage("Initializing MQTT Communication Manager", true);
    }

    void Start()
    {
        LogMessage($"Target broker: {brokerAddress}:{brokerPort}", true);

        if (connectOnStart)
        {
            // Add a small delay before connecting
            m_NextReconnectTime = Time.time + 0.5f;
        }
    }

    void OnDestroy()
    {
        LogMessage("MQTT Manager being destroyed - disconnecting client", true);
        Disconnect();
    }

    void OnApplicationQuit()
    {
        LogMessage("Application quitting - disconnecting MQTT client", true);
        Disconnect();
    }

    void Update()
    {
        // Execute any queued main thread actions
        ExecuteMainThreadActions();

        // Process any queued MQTT messages
        ProcessQueuedMessages();

        // Check if we need to reconnect
        CheckForReconnect();

        // Check if we need to ping the broker
        CheckForPing();
    }

    private void ExecuteMainThreadActions()
    {
        lock (_mainThreadActionsLock)
        {
            while (_mainThreadActions.Count > 0)
            {
                var action = _mainThreadActions.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    LogError($"Error executing main thread action: {e.Message}");
                }
            }
        }
    }

    private void ProcessQueuedMessages()
    {
        lock (_messageQueue)
        {
            while (_messageQueue.Count > 0)
            {
                var message = _messageQueue.Dequeue();
                ProcessMessageOnMainThread(message.Topic, message.Payload);
            }
        }
    }

    private void CheckForReconnect()
    {
        if (!m_IsConnected && !m_IsConnecting && Time.time > m_NextReconnectTime)
        {
            StartConnectionProcess();
        }
    }

    private void CheckForPing()
    {
        if (m_IsConnected && Time.time > m_NextPingTime)
        {
            SendPing();
            m_NextPingTime = Time.time + 30f; // Ping every 30 seconds
        }
    }

    #region Connection Management

    /// <summary>
    /// Starts the connection process and will automatically reconnect until successful
    /// </summary>
    public void StartConnectionProcess()
    {
        lock (m_LockObject)
        {
            if (m_IsConnecting)
                return;

            m_ReconnectAttempts = 0;
            m_CurrentReconnectInterval = initialReconnectInterval;
            m_LastConnectionError = "";

            Connect();
        }
    }

    /// <summary>
    /// Attempts to connect to the MQTT broker without using coroutines
    /// </summary>
    public void Connect()
    {
        lock (m_LockObject)
        {
            if (m_IsConnecting)
            {
                LogMessage("Connection attempt already in progress", true);
                return;
            }

            if (m_IsConnected && mqttClient != null && mqttClient.IsConnected)
            {
                LogMessage("Already connected", true);
                return;
            }

            m_IsConnecting = true;
        }

        m_LastConnectionAttempt = DateTime.Now;

        LogMessage(
            $"Checking network connection to {brokerAddress}:{brokerPort} (Attempt {m_ReconnectAttempts + 1})",
            true
        );

        // First check if we have internet connectivity at all
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            LogWarning("No internet connection available");
            OnConnectionFailure("No internet connection available");
            return;
        }

        // Start connection process on a background thread
        ThreadPool.QueueUserWorkItem(ConnectionThreadWorker);
    }

    private void ConnectionThreadWorker(object state)
    {
        bool success = false;
        string errorMessage = "";

        try
        {
            // Test TCP connection first
            LogMessage("Testing TCP connection to broker", true);

            using (var tcpClient = new TcpClient())
            {
                // Try to connect with timeout
                var connectResult = tcpClient.BeginConnect(brokerAddress, brokerPort, null, null);
                bool connected = connectResult.AsyncWaitHandle.WaitOne(5000); // 5 second timeout

                if (!connected || !tcpClient.Connected)
                {
                    errorMessage =
                        $"Could not establish TCP connection to {brokerAddress}:{brokerPort}";
                    success = false;
                    return;
                }

                LogMessage("TCP connection test successful", true);
            }

            // TCP test succeeded, now try MQTT connection

            // Clean up any existing client
            MqttClient oldClient = null;
            lock (m_LockObject)
            {
                oldClient = mqttClient;
                mqttClient = null;
            }

            if (oldClient != null)
            {
                try
                {
                    if (oldClient.IsConnected)
                        oldClient.Disconnect();
                }
                catch (Exception e)
                {
                    LogWarning($"Error cleaning up existing MQTT client: {e.Message}");
                }
            }

            // Create new MQTT client
            MqttClient newClient = new MqttClient(
                brokerAddress,
                brokerPort,
                false,
                null,
                null,
                MqttSslProtocols.None
            );

            // Generate a unique client ID to avoid conflicts
            string clientIdWithSuffix = clientId + "-" + Guid.NewGuid().ToString().Substring(0, 8);
            LogMessage($"Using client ID: {clientIdWithSuffix}", true);

            // Connect with or without credentials
            byte connectionResult;
            if (string.IsNullOrEmpty(username))
                connectionResult = newClient.Connect(clientIdWithSuffix);
            else
                connectionResult = newClient.Connect(clientIdWithSuffix, username, password);

            success = newClient.IsConnected && connectionResult == 0;

            if (success)
            {
                LogMessage($"Connection successful: {connectionResult}", true);

                // Set up event handlers
                newClient.MqttMsgPublishReceived += OnMqttMessageReceived;
                newClient.ConnectionClosed += OnConnectionClosed;

                // Store the new client and connection state
                lock (m_LockObject)
                {
                    mqttClient = newClient;
                    m_IsConnected = true;
                    m_IsConnecting = false;
                    m_ClientIdWithSuffix = clientIdWithSuffix;
                }

                // Queue actions for main thread
                QueueOnMainThread(() =>
                {
                    m_NextPingTime = Time.time + 30f; // Schedule first ping
                    ProcessPendingSubscriptions();
                    ResubscribeToAllTopics();

                    // Notify listeners that we're connected
                    LogMessage("Successfully connected to MQTT broker", true);
                    onConnected?.Invoke();
                });
            }
            else
            {
                errorMessage = GetConnectionErrorMessage(connectionResult);
                LogError($"Failed to connect. Return code: {connectionResult} - {errorMessage}");
            }
        }
        catch (Exception e)
        {
            success = false;
            errorMessage = e.Message;
            LogError($"Error in connection process: {e.Message}");
        }

        if (!success)
        {
            OnConnectionFailure(errorMessage);
        }
    }

    private string GetConnectionErrorMessage(byte connectionResult)
    {
        // Connection result codes from MQTT spec
        switch (connectionResult)
        {
            case 0:
                return "Connection Accepted";
            case 1:
                return "Connection Refused: unacceptable protocol version";
            case 2:
                return "Connection Refused: identifier rejected";
            case 3:
                return "Connection Refused: server unavailable";
            case 4:
                return "Connection Refused: bad user name or password";
            case 5:
                return "Connection Refused: not authorized";
            default:
                return "Unknown error";
        }
    }

    private void OnConnectionFailure(string reason)
    {
        lock (m_LockObject)
        {
            m_IsConnected = false;
            m_IsConnecting = false;
            m_LastConnectionError = reason;
            mqttClient = null;
        }

        LogError($"Connection failed: {reason}");

        // Schedule reconnection on main thread
        QueueOnMainThread(() =>
        {
            if (onConnectionFailed != null)
                onConnectionFailed.Invoke(reason);

            ScheduleReconnection();
        });
    }

    private void OnConnectionClosed(object sender, EventArgs e)
    {
        bool wasConnected = false;

        lock (m_LockObject)
        {
            wasConnected = m_IsConnected;
            m_IsConnected = false;
        }

        if (wasConnected)
        {
            LogWarning("Connection closed unexpectedly");

            // Schedule processing on main thread
            QueueOnMainThread(() =>
            {
                if (onConnectionLost != null)
                    onConnectionLost.Invoke();

                ScheduleReconnection();
            });
        }
    }

    private void ScheduleReconnection()
    {
        // Check max reconnect attempts
        if (maxReconnectAttempts > 0 && m_ReconnectAttempts >= maxReconnectAttempts)
        {
            LogError($"Maximum reconnection attempts ({maxReconnectAttempts}) reached. Giving up.");
            return;
        }

        m_ReconnectAttempts++;

        // Use exponential backoff with a cap
        float delay = Mathf.Min(m_CurrentReconnectInterval, maxReconnectInterval);
        m_CurrentReconnectInterval *= 1.5f;

        LogMessage(
            $"Scheduling reconnection in {delay:F1} seconds (Attempt {m_ReconnectAttempts})",
            true
        );

        // Schedule the next reconnection time
        m_NextReconnectTime = Time.time + delay;
    }

    private void SendPing()
    {
        if (!IsConnected)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (m_LockObject)
                {
                    if (mqttClient != null && mqttClient.IsConnected)
                    {
                        // Instead of using Ping() which is protected, use a dummy publish to check connection
                        mqttClient.Publish("$SYS/ping", new byte[0], 0, false);
                        LogMessage("Connection check sent to broker", false);
                    }
                }
            }
            catch (Exception e)
            {
                LogWarning($"Error checking connection to broker: {e.Message}");

                bool wasConnected = false;
                lock (m_LockObject)
                {
                    wasConnected = m_IsConnected;
                    m_IsConnected = false;
                }

                if (wasConnected)
                {
                    QueueOnMainThread(() =>
                    {
                        if (onConnectionLost != null)
                            onConnectionLost.Invoke();

                        ScheduleReconnection();
                    });
                }
            }
        });
    }

    public void Disconnect()
    {
        MqttClient clientToDisconnect = null;

        lock (m_LockObject)
        {
            if (!m_IsConnected || mqttClient == null)
                return;

            clientToDisconnect = mqttClient;
            m_IsConnected = false;
            m_IsConnecting = false;
            mqttClient = null;
        }

        if (clientToDisconnect != null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (clientToDisconnect.IsConnected)
                    {
                        clientToDisconnect.Disconnect();
                    }

                    LogMessage("Disconnected from MQTT broker", true);
                }
                catch (Exception e)
                {
                    LogError($"Error disconnecting from broker: {e.Message}");
                }
            });
        }
    }

    public void CheckConnection()
    {
        if (IsConnected)
        {
            LogMessage($"Currently connected to broker at {brokerAddress}:{brokerPort}", true);
        }
        else if (IsConnecting)
        {
            LogMessage(
                $"Currently attempting to connect to broker at {brokerAddress}:{brokerPort}",
                true
            );
        }
        else
        {
            LogMessage($"Not connected to broker at {brokerAddress}:{brokerPort}", true);
            StartConnectionProcess();
        }
    }

    public void SetBrokerSettings(string address, int port)
    {
        bool settingsChanged = address != brokerAddress || port != brokerPort;

        if (settingsChanged)
        {
            brokerAddress = address;
            brokerPort = port;

            // Reconnect if connected
            if (IsConnected)
            {
                LogMessage("Broker settings changed, reconnecting...", true);
                Disconnect();
                StartConnectionProcess();
            }
        }
    }

    #endregion

    #region Message Handling

    public void Publish(string topic, string message)
    {
        if (!IsConnected)
        {
            LogWarning($"Cannot publish to {topic}: MQTT client not connected");
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (m_LockObject)
                {
                    if (mqttClient != null && mqttClient.IsConnected)
                    {
                        mqttClient.Publish(
                            topic,
                            Encoding.UTF8.GetBytes(message),
                            MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                            false
                        );

                        LogMessage(
                            $"Published to {topic}: {(logMessageContent ? message : "[Message content hidden]")}",
                            true
                        );
                    }
                    else
                    {
                        LogWarning($"Cannot publish to {topic}: MQTT client not connected");
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error publishing to topic {topic}: {e.Message}");

                // Check if connection was lost
                bool wasConnected = false;
                lock (m_LockObject)
                {
                    wasConnected = m_IsConnected;
                    m_IsConnected = false;
                }

                if (wasConnected)
                {
                    QueueOnMainThread(() =>
                    {
                        if (onConnectionLost != null)
                            onConnectionLost.Invoke();

                        ScheduleReconnection();
                    });
                }
            }
        });
    }

    public void Subscribe(string topic, Action<string> callback)
    {
        // Store the callback
        lock (m_LockObject)
        {
            if (topicCallbacks.ContainsKey(topic))
                topicCallbacks[topic] = callback;
            else
                topicCallbacks.Add(topic, callback);
        }

        if (!IsConnected)
        {
            LogWarning($"Queuing subscription to {topic} until MQTT client is connected");

            lock (m_LockObject)
            {
                // Check if already in pending list
                bool alreadyPending = false;
                foreach (var item in pendingSubscriptions)
                {
                    if (item.Key == topic)
                    {
                        alreadyPending = true;
                        break;
                    }
                }

                // Store for later when we're connected
                if (!alreadyPending)
                    pendingSubscriptions.Add(
                        new KeyValuePair<string, Action<string>>(topic, callback)
                    );
            }

            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (m_LockObject)
                {
                    if (mqttClient != null && mqttClient.IsConnected)
                    {
                        mqttClient.Subscribe(
                            new string[] { topic },
                            new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE }
                        );

                        LogMessage($"Subscribed to topic: {topic}", true);
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error subscribing to topic {topic}: {e.Message}");

                // Check if connection was lost
                bool wasConnected = false;
                lock (m_LockObject)
                {
                    wasConnected = m_IsConnected;
                    m_IsConnected = false;
                }

                if (wasConnected)
                {
                    QueueOnMainThread(() =>
                    {
                        if (onConnectionLost != null)
                            onConnectionLost.Invoke();

                        ScheduleReconnection();
                    });
                }
            }
        });
    }

    public void Unsubscribe(string topic)
    {
        lock (m_LockObject)
        {
            // Remove the callback
            if (topicCallbacks.ContainsKey(topic))
                topicCallbacks.Remove(topic);

            // Remove from pending subscriptions if present
            pendingSubscriptions.RemoveAll(pair => pair.Key == topic);
        }

        if (!IsConnected)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (m_LockObject)
                {
                    if (mqttClient != null && mqttClient.IsConnected)
                    {
                        mqttClient.Unsubscribe(new string[] { topic });
                        LogMessage($"Unsubscribed from topic: {topic}", true);
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error unsubscribing from topic {topic}: {e.Message}");
            }
        });
    }

    private void ProcessPendingSubscriptions()
    {
        List<string> topics = new List<string>();
        List<byte> qosLevels = new List<byte>();

        // Extract pending subscriptions while holding the lock
        lock (m_LockObject)
        {
            if (!m_IsConnected || mqttClient == null || pendingSubscriptions.Count == 0)
                return;

            LogMessage(
                $"Processing {pendingSubscriptions.Count} pending topic subscriptions",
                true
            );

            // Group the subscriptions
            foreach (var subscription in pendingSubscriptions)
            {
                topics.Add(subscription.Key);
                qosLevels.Add(MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE);
            }

            pendingSubscriptions.Clear();
        }

        // Subscribe on a background thread
        if (topics.Count > 0)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (m_LockObject)
                    {
                        if (mqttClient != null && mqttClient.IsConnected)
                        {
                            mqttClient.Subscribe(topics.ToArray(), qosLevels.ToArray());
                            LogMessage($"Subscribed to {topics.Count} pending topics", true);
                        }
                    }
                }
                catch (Exception e)
                {
                    LogError($"Error subscribing to pending topics: {e.Message}");
                }
            });
        }
    }

    private void ResubscribeToAllTopics()
    {
        List<string> topics = new List<string>();
        List<byte> qosLevels = new List<byte>();

        // Extract topics while holding the lock
        lock (m_LockObject)
        {
            if (!m_IsConnected || mqttClient == null || topicCallbacks.Count == 0)
                return;

            LogMessage($"Resubscribing to {topicCallbacks.Count} topics", true);

            foreach (string topic in topicCallbacks.Keys)
            {
                topics.Add(topic);
                qosLevels.Add(MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE);
            }
        }

        // Subscribe on a background thread
        if (topics.Count > 0)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (m_LockObject)
                    {
                        if (mqttClient != null && mqttClient.IsConnected)
                        {
                            mqttClient.Subscribe(topics.ToArray(), qosLevels.ToArray());
                            LogMessage($"Resubscribed to {topics.Count} topics", true);
                        }
                    }
                }
                catch (Exception e)
                {
                    LogError($"Error resubscribing to topics: {e.Message}");
                }
            });
        }
    }

    private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs e)
    {
        string topic = e.Topic;
        string message = Encoding.UTF8.GetString(e.Message);

        // We're in a background thread here, so we need to queue the message for processing in Update
        try
        {
            lock (_messageQueue)
            {
                _messageQueue.Enqueue(new MqttMessage { Topic = topic, Payload = message });
            }
        }
        catch (Exception ex)
        {
            // Can't do much in a background thread other than log this
            Debug.LogException(ex);
        }
    }

    // Safely process the message on the main thread
    private void ProcessMessageOnMainThread(string topic, string message)
    {
        LogMessage(
            $"Message received on topic {topic}: {(logMessageContent ? message : "[Content hidden]")}",
            true
        );

        // Invoke the generic message received event
        if (onMessageReceived != null)
            onMessageReceived.Invoke(topic, message);

        // Process message with registered callback
        Action<string> callback = null;
        lock (m_LockObject)
        {
            if (topicCallbacks.TryGetValue(topic, out callback) && callback != null)
            {
                try
                {
                    callback(message);
                }
                catch (Exception e)
                {
                    LogError($"Error in callback for topic {topic}: {e.Message}");
                }
            }
        }
    }

    // Helper method to queue actions to be executed on the main thread
    private void QueueOnMainThread(Action action)
    {
        if (action == null)
            return;

        lock (_mainThreadActionsLock)
        {
            _mainThreadActions.Enqueue(action);
        }
    }

    #endregion

    #region Logging Utilities

    private void LogMessage(string message, bool forceLog = false)
    {
        if (debugMode && (forceLog || verboseLogging))
            Debug.Log($"[MQTT] {message}");
    }

    private void LogWarning(string message)
    {
        if (debugMode)
            Debug.LogWarning($"[MQTT] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[MQTT] {message}");
    }

    private void LogException(Exception e)
    {
        if (debugMode)
            Debug.LogException(e);
    }

    #endregion

    #region Diagnostic Methods

    /// <summary>
    /// Returns diagnostic information about the MQTT client
    /// </summary>
    public string GetDiagnosticInfo()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== MQTT Diagnostic Information =====");
        sb.AppendLine($"Broker Address: {brokerAddress}:{brokerPort}");

        lock (m_LockObject)
        {
            sb.AppendLine($"Connected: {m_IsConnected}");
            sb.AppendLine($"Is Connecting: {m_IsConnecting}");

            if (mqttClient != null)
                sb.AppendLine(
                    $"Client Internal State: {(mqttClient.IsConnected ? "Connected" : "Disconnected")}"
                );
            else
                sb.AppendLine("Client: Not created");

            sb.AppendLine($"Client ID: {m_ClientIdWithSuffix}");
            sb.AppendLine(
                $"Last Connection Attempt: {m_LastConnectionAttempt.ToString("yyyy-MM-dd HH:mm:ss")}"
            );
            sb.AppendLine($"Reconnect Attempts: {m_ReconnectAttempts}");

            if (!string.IsNullOrEmpty(m_LastConnectionError))
                sb.AppendLine($"Last Error: {m_LastConnectionError}");

            sb.AppendLine($"Active Subscriptions: {topicCallbacks.Count}");
            sb.AppendLine($"Pending Subscriptions: {pendingSubscriptions.Count}");
        }

        // Network info
        sb.AppendLine($"Internet Reachability: {Application.internetReachability}");

        return sb.ToString();
    }

    /// <summary>
    /// Log detailed diagnostic information to the console
    /// </summary>
    public void LogDiagnosticInfo()
    {
        Debug.Log(GetDiagnosticInfo());
    }

    #endregion
}
