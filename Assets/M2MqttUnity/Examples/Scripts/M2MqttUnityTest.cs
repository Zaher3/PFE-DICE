    using UnityEngine;
using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using M2MqttUnity;
using TMPro;

public class ModifiedM2MqttUnityTest : M2MqttUnityClient
{
    [SerializeField] private TMP_Text[] uiTexts = new TMP_Text[28]; // Array of 28 TextMeshPro elements
    private Queue<MqttMsgPublishEventArgs> messageQueue = new Queue<MqttMsgPublishEventArgs>();
    private bool isMessageProcessing = false;
    protected override void OnConnecting()
    {
        base.OnConnecting();
        SetUiMessage("Connecting to broker on " + brokerAddress + ":" + brokerPort.ToString() + "...\n");
    }

    protected override void OnConnected()
    {
        Debug.Log($"Connected to broker on {brokerAddress}:{brokerPort}");
        SetUiMessage("Connected to broker on " + brokerAddress + "\n");
        SubscribeTopics();
    }

    protected override void SubscribeTopics()
    {
    try
    {
        Debug.Log("Subscribing to topics");
        SubscribeTopicBatch("Motor1");
        SubscribeTopicBatch("Motor2");
        SubscribeTopicBatch("Motor3");
        SubscribeTopicBatch("Motor4");
        Debug.Log("Subscribed to all topics");
    }
    catch (Exception e)
    {
        Debug.LogError($"Error subscribing to topics: {e.Message}\nStackTrace: {e.StackTrace}");
    }
    }

private void SubscribeTopicBatch(string motorPrefix)
{
    string[] topics = new string[7];
    byte[] qosLevels = new byte[7];
    for (int i = 0; i < 7; i++)
    {
        topics[i] = $"{motorPrefix}/Data{i+1}";
        qosLevels[i] = 0;
    }
    client.Subscribe(topics, qosLevels);
    Debug.Log($"Subscribed to {motorPrefix} topics");
}

    protected override void UnsubscribeTopics()
    {
        client.Unsubscribe(new string[] { 
            "Motor1/Data1", "Motor1/Data2", "Motor1/Data3", "Motor1/Data4", "Motor1/Data5", "Motor1/Data6", "Motor1/Data7",
            "Motor2/Data1", "Motor2/Data2", "Motor2/Data3", "Motor2/Data4", "Motor2/Data5", "Motor2/Data6", "Motor2/Data7",
            "Motor3/Data1", "Motor3/Data2", "Motor3/Data3", "Motor3/Data4", "Motor3/Data5", "Motor3/Data6", "Motor3/Data7",
            "Motor4/Data1", "Motor4/Data2", "Motor4/Data3", "Motor4/Data4", "Motor4/Data5", "Motor4/Data6", "Motor4/Data7"
        });
    }

    protected override void OnConnectionFailed(string errorMessage)
    {
        Debug.LogError("CONNECTION FAILED! " + errorMessage);
    }

    protected override void OnDisconnected()
    {
        Debug.Log("Disconnected.");
        
    }

    protected override void OnConnectionLost()
    {
        Debug.LogError("CONNECTION LOST!");
    }

    protected override void DecodeMessage(string topic, byte[] message)
    {
        MqttMsgPublishEventArgs msg = new MqttMsgPublishEventArgs(topic, message, false, 0, false);
        lock (messageQueue)
        {
            messageQueue.Enqueue(msg);
        }
        if (!isMessageProcessing)
        {
            ProcessMessages();
        }
    }

    private void ProcessMessages()
    {
        isMessageProcessing = true;
        MqttMsgPublishEventArgs msg;
        while (messageQueue.Count > 0)
        {
            lock (messageQueue)
            {
                msg = messageQueue.Dequeue();
            }
            string decodedMsg = System.Text.Encoding.UTF8.GetString(msg.Message);
            Debug.Log($"Received message on topic: {msg.Topic}, Content: {decodedMsg}");
            StoreMessage(msg.Topic, decodedMsg);
        }
        isMessageProcessing = false;
    }

    private void StoreMessage(string topic, string msg)
    {
    string[] parts = topic.Split('/');
    if (parts.Length != 2)
    {
        Debug.LogError($"Invalid topic format: {topic}");
        return;
    }

    int motorNumber = int.Parse(parts[0].Substring(5)) - 1; // "Motor1" -> 0, "Motor2" -> 1, etc.
    int dataNumber = int.Parse(parts[1].Substring(4)) - 1; // "Data1" -> 0, "Data2" -> 1, etc.

    int index = motorNumber * 7 + dataNumber;
    Debug.Log($"Attempting to update UI Text at index: {index} with message: {msg}");
    
    if (index >= 0 && index < uiTexts.Length)
    {
        if (uiTexts[index] != null)
        {
            uiTexts[index].text = msg;
            Debug.Log($"Updated UI Text at index: {index}");
        }
        else
        {
            Debug.LogError($"UI Text at index {index} is null");
        }
    }
    else
    {
        Debug.LogError($"Index {index} out of range for uiTexts array");
    }
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void SetUiMessage(string message)
    {
        Debug.Log(message);
    }
}