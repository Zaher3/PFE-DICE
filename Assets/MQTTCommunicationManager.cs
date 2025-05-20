using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

public class MQTTCommunicationManager : MonoBehaviour
{
    [SerializeField]
    [Tooltip("MQTT broker address")]
    private string brokerAddress = "localhost";

    [SerializeField]
    [Tooltip("MQTT broker port")]
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

    [SerializeField]
    [Tooltip("Auto-connect on start")]
    private bool connectOnStart = true;

    [SerializeField]
    [Tooltip("Debug mode to log all MQTT events")]
    private bool debugMode = true;

    [SerializeField]
    [Tooltip("Initial reconnection interval in seconds")]
    private float initialReconnectInterval = 1.0f;

    [SerializeField]
    [Tooltip("Maximum reconnection interval in seconds")]
    private float maxReconnectInterval = 60.0f;

    [SerializeField]
    [Tooltip("Connection timeout in seconds")]
    private float connectionTimeout = 10.0f;

    [SerializeField]
    [Tooltip("Max number of reconnection attempts (0 = infinite)")]
    private int maxReconnectAttempts = 0;

    private bool m_IsConnected = false;
    private bool m_IsConnecting = false;
    private int m_ReconnectAttempts = 0;
    private float m_CurrentReconnectInterval;
    private Coroutine m_ConnectionCoroutine;
    private Coroutine m_ReconnectCoroutine;

    // Event that fires when MQTT client successfully connects
    public event Action onConnected;

    // Event that fires when MQTT client fails to connect
    public event Action<string> onConnectionFailed;

    // Event that fires when connection is lost
    public event Action onConnectionLost;

    public bool IsConnected => m_IsConnected;
    public bool IsConnecting => m_IsConnecting;
    public string BrokerAddress => brokerAddress;
    public int BrokerPort => brokerPort;
    public int ReconnectAttempts => m_ReconnectAttempts;

    private MqttClient mqttClient;

    // Dictionary to store topic subscription callbacks
    private Dictionary<string, Action<string>> topicCallbacks =
        new Dictionary<string, Action<string>>();

    // List to store pending subscriptions when client isn't connected yet
    private List<KeyValuePair<string, Action<string>>> pendingSubscriptions =
        new List<KeyValuePair<string, Action<string>>>();

    void Start()
    {
        Debug.Log("[MQTT] Initializing MQTT Communication Manager...");

        if (connectOnStart)
        {
            // Add a slight delay to ensure everything is initialized
            StartCoroutine(ConnectWithDelay(0.5f));
        }
    }

    private IEnumerator ConnectWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        StartConnectionProcess();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    /// <summary>
    /// Starts the connection process and will automatically reconnect until successful
    /// </summary>
    public void StartConnectionProcess()
    {
        // Cancel any existing connection attempts
        StopConnectionAttempts();

        m_ReconnectAttempts = 0;
        m_CurrentReconnectInterval = initialReconnectInterval;

        // Start the connection process
        Connect();
    }

    /// <summary>
    /// Stop any ongoing connection attempts
    /// </summary>
    public void StopConnectionAttempts()
    {
        if (m_ConnectionCoroutine != null)
        {
            StopCoroutine(m_ConnectionCoroutine);
            m_ConnectionCoroutine = null;
        }

        if (m_ReconnectCoroutine != null)
        {
            StopCoroutine(m_ReconnectCoroutine);
            m_ReconnectCoroutine = null;
        }

        m_IsConnecting = false;
    }

    /// <summary>
    /// Attempts to connect to the MQTT broker once
    /// </summary>
    public void Connect()
    {
        if (m_IsConnecting)
        {
            Debug.Log("[MQTT] Connection attempt already in progress");
            return;
        }

        if (m_IsConnected)
        {
            Debug.Log("[MQTT] Already connected");
            return;
        }

        try
        {
            m_IsConnecting = true;
            Debug.Log(
                $"[MQTT] Attempting to connect to broker at {brokerAddress}:{brokerPort} (Attempt {m_ReconnectAttempts + 1})"
            );

            // Start the connection coroutine
            m_ConnectionCoroutine = StartCoroutine(ConnectWithTimeout());
        }
        catch (Exception e)
        {
            m_IsConnecting = false;
            Debug.LogError("[MQTT] Error initiating connection: " + e.Message);
            Debug.LogException(e);

            // Schedule reconnection
            ScheduleReconnection();
        }
    }

    private IEnumerator ConnectWithTimeout()
    {
        float startTime = Time.time;
        bool timedOut = false;
        bool connectionComplete = false;
        byte connectionResult = 0;
        Exception connectionException = null;
        string clientIdWithSuffix = "";

        // Try to create the MQTT client
        try
        {
            mqttClient = new MqttClient(
                brokerAddress,
                brokerPort,
                false,
                null,
                null,
                MqttSslProtocols.None
            );

            // Set up message received callback
            mqttClient.MqttMsgPublishReceived += OnMqttMessageReceived;
            mqttClient.ConnectionClosed += OnConnectionClosed;

            // Generate a unique client ID to avoid conflicts
            clientIdWithSuffix = clientId + "-" + Guid.NewGuid().ToString().Substring(0, 8);
            Debug.Log($"[MQTT] Using client ID: {clientIdWithSuffix}");
        }
        catch (Exception e)
        {
            Debug.LogError("[MQTT] Error creating MQTT client: " + e.Message);
            OnConnectionFailure("Error creating MQTT client: " + e.Message);
            yield break;
        }

        // Use a thread to perform the connection
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // Connect with or without credentials
                if (string.IsNullOrEmpty(username))
                    connectionResult = mqttClient.Connect(clientIdWithSuffix);
                else
                    connectionResult = mqttClient.Connect(clientIdWithSuffix, username, password);
            }
            catch (Exception e)
            {
                connectionException = e;
            }
            connectionComplete = true;
        });

        // Wait for connection attempt to complete or time out
        while (!connectionComplete && !timedOut)
        {
            timedOut = (Time.time - startTime) > connectionTimeout;
            yield return null;
        }

        if (timedOut)
        {
            Debug.LogError(
                $"[MQTT] Connection attempt timed out after {connectionTimeout} seconds"
            );
            mqttClient = null;
            OnConnectionFailure("Connection attempt timed out");
            yield break;
        }

        if (connectionException != null)
        {
            Debug.LogError("[MQTT] Error connecting to broker: " + connectionException.Message);
            OnConnectionFailure("Error connecting to broker: " + connectionException.Message);
            yield break;
        }

        m_IsConnected = mqttClient != null && mqttClient.IsConnected;
        Debug.Log($"[MQTT] Connection result: {connectionResult}, IsConnected: {m_IsConnected}");

        if (!m_IsConnected)
        {
            Debug.LogError($"[MQTT] Failed to connect. Return code: {connectionResult}");
            OnConnectionFailure($"Connection failed with code: {connectionResult}");
            yield break;
        }

        // Connection successful
        m_ReconnectAttempts = 0;
        m_CurrentReconnectInterval = initialReconnectInterval;
        m_IsConnecting = false;

        // Process any pending subscriptions
        ProcessPendingSubscriptions();

        // Resubscribe to any topics
        ResubscribeToAllTopics();

        // Notify listeners that we're connected
        Debug.Log("[MQTT] Successfully connected to broker");
        if (onConnected != null)
            onConnected.Invoke();
    }

    private void OnConnectionFailure(string reason)
    {
        m_IsConnected = false;
        m_IsConnecting = false;
        mqttClient = null;

        Debug.LogError($"[MQTT] Connection failed: {reason}");

        if (onConnectionFailed != null)
            onConnectionFailed.Invoke(reason);

        // Schedule reconnection
        ScheduleReconnection();
    }

    private void OnConnectionClosed(object sender, EventArgs e)
    {
        if (m_IsConnected)
        {
            Debug.LogWarning("[MQTT] Connection closed unexpectedly");
            m_IsConnected = false;

            if (onConnectionLost != null)
                onConnectionLost.Invoke();

            // Schedule reconnection
            ScheduleReconnection();
        }
    }

    private void ScheduleReconnection()
    {
        // Check max reconnect attempts
        if (maxReconnectAttempts > 0 && m_ReconnectAttempts >= maxReconnectAttempts)
        {
            Debug.LogError(
                $"[MQTT] Maximum reconnection attempts ({maxReconnectAttempts}) reached. Giving up."
            );
            return;
        }

        m_ReconnectAttempts++;

        // Use exponential backoff with a cap
        float delay = Mathf.Min(m_CurrentReconnectInterval, maxReconnectInterval);
        m_CurrentReconnectInterval *= 1.5f;

        Debug.Log(
            $"[MQTT] Scheduling reconnection in {delay:F1} seconds (Attempt {m_ReconnectAttempts})"
        );

        if (m_ReconnectCoroutine != null)
            StopCoroutine(m_ReconnectCoroutine);

        m_ReconnectCoroutine = StartCoroutine(ReconnectAfterDelay(delay));
    }

    private IEnumerator ReconnectAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log($"[MQTT] Attempting to reconnect...");
        Connect();
    }

    public void Publish(string topic, string message)
    {
        if (!IsConnected || mqttClient == null)
        {
            LogWarning($"Cannot publish to {topic}: MQTT client not connected");
            return;
        }

        try
        {
            mqttClient.Publish(
                topic,
                Encoding.UTF8.GetBytes(message),
                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                false
            );
            LogMessage($"Published to {topic}: {message}");
        }
        catch (Exception e)
        {
            LogError($"Error publishing to topic {topic}: {e.Message}");
        }
    }

    public void Subscribe(string topic, Action<string> callback)
    {
        // Store the callback
        if (topicCallbacks.ContainsKey(topic))
            topicCallbacks[topic] = callback;
        else
            topicCallbacks.Add(topic, callback);

        if (!IsConnected || mqttClient == null)
        {
            LogWarning($"Queuing subscription to {topic} until MQTT client is connected");

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
                pendingSubscriptions.Add(new KeyValuePair<string, Action<string>>(topic, callback));

            return;
        }

        try
        {
            mqttClient.Subscribe(
                new string[] { topic },
                new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE }
            );
            LogMessage($"Subscribed to topic: {topic}");
        }
        catch (Exception e)
        {
            LogError($"Error subscribing to topic {topic}: {e.Message}");
        }
    }

    public void Unsubscribe(string topic)
    {
        // Remove the callback
        if (topicCallbacks.ContainsKey(topic))
            topicCallbacks.Remove(topic);

        // Remove from pending subscriptions if present
        pendingSubscriptions.RemoveAll(pair => pair.Key == topic);

        if (!IsConnected || mqttClient == null)
            return;

        try
        {
            mqttClient.Unsubscribe(new string[] { topic });
            LogMessage($"Unsubscribed from topic: {topic}");
        }
        catch (Exception e)
        {
            LogError($"Error unsubscribing from topic {topic}: {e.Message}");
        }
    }

    private void ProcessPendingSubscriptions()
    {
        if (!IsConnected || mqttClient == null || pendingSubscriptions.Count == 0)
            return;

        LogMessage($"Processing {pendingSubscriptions.Count} pending topic subscriptions");

        // Group the subscriptions
        List<string> topics = new List<string>();
        List<byte> qosLevels = new List<byte>();

        foreach (var subscription in pendingSubscriptions)
        {
            topics.Add(subscription.Key);
            qosLevels.Add(MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE);
        }

        try
        {
            mqttClient.Subscribe(topics.ToArray(), qosLevels.ToArray());
            LogMessage($"Subscribed to {topics.Count} pending topics");
            pendingSubscriptions.Clear();
        }
        catch (Exception e)
        {
            LogError($"Error subscribing to pending topics: {e.Message}");
        }
    }

    private void ResubscribeToAllTopics()
    {
        if (mqttClient != null && mqttClient.IsConnected && topicCallbacks.Count > 0)
        {
            string[] topics = new string[topicCallbacks.Count];
            byte[] qosLevels = new byte[topicCallbacks.Count];

            int i = 0;
            foreach (string topic in topicCallbacks.Keys)
            {
                topics[i] = topic;
                qosLevels[i] = MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE;
                i++;
            }

            mqttClient.Subscribe(topics, qosLevels);
            LogMessage($"Resubscribed to {topicCallbacks.Count} topics");
        }
    }

    private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs e)
    {
        string topic = e.Topic;
        string message = Encoding.UTF8.GetString(e.Message);

        // Process on the main thread to avoid threading issues
        UnityMainThreadDispatcher
            .Instance()
            .Enqueue(() =>
            {
                LogMessage($"Message received on topic {topic}: {message}");
                ProcessMessage(topic, message);
            });
    }

    private void ProcessMessage(string topic, string message)
    {
        if (topicCallbacks.TryGetValue(topic, out Action<string> callback))
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

    public void Disconnect()
    {
        // Stop any reconnection attempts
        StopConnectionAttempts();

        if (!m_IsConnected || mqttClient == null)
            return;

        try
        {
            if (mqttClient.IsConnected)
            {
                mqttClient.Disconnect();
            }

            m_IsConnected = false;
            Debug.Log("[MQTT] Disconnected from broker");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MQTT] Error disconnecting from broker: {e.Message}");
        }
    }

    /// <summary>
    /// Check the current connection status and attempt to connect if not connected
    /// </summary>
    public void CheckConnection()
    {
        if (m_IsConnected && mqttClient != null && mqttClient.IsConnected)
        {
            Debug.Log($"[MQTT] Currently connected to broker at {brokerAddress}:{brokerPort}");
        }
        else if (m_IsConnecting)
        {
            Debug.Log(
                $"[MQTT] Currently attempting to connect to broker at {brokerAddress}:{brokerPort}"
            );
        }
        else
        {
            Debug.Log($"[MQTT] Not connected to broker at {brokerAddress}:{brokerPort}");
            StartConnectionProcess();
        }
    }

    /// <summary>
    /// Set new broker address and port - will reconnect if already connected
    /// </summary>
    public void SetBrokerSettings(string address, int port)
    {
        bool settingsChanged = address != brokerAddress || port != brokerPort;

        if (settingsChanged)
        {
            brokerAddress = address;
            brokerPort = port;

            // Reconnect if connected
            if (m_IsConnected)
            {
                Debug.Log("[MQTT] Broker settings changed, reconnecting...");
                Disconnect();
                StartConnectionProcess();
            }
        }
    }

    // Utility methods for logging
    private void LogMessage(string message)
    {
        if (debugMode)
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

    // Helper class to execute actions on the main thread
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();
        private static UnityMainThreadDispatcher _instance = null;

        public static UnityMainThreadDispatcher Instance()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }

        public void Enqueue(Action action)
        {
            lock (_executionQueue)
            {
                _executionQueue.Enqueue(action);
            }
        }

        void Update()
        {
            lock (_executionQueue)
            {
                while (_executionQueue.Count > 0)
                {
                    _executionQueue.Dequeue().Invoke();
                }
            }
        }
    }
}
