//Joe Snider
//1/20
//
//Coordinate streaming data across the network.
//1 object stream, and mark server/client (make sure there's only 1 server).
//   The server owns all the objects and tells the clients where they are.
//2hands and 1 head stream are owned by each instance and broadcast to all listeners.
//

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using LSL;

public class LSLServer : MonoBehaviour
{
    [System.Serializable]
    public struct streamData {
        public GameObject gameObject;
        //index into streamableObjects, no checking
        public int type;
    };
    //put all the objects to stream here. The server owns all the objects, each client owns the head/hands.
    //one head and two hands hacked in
    [Header("head and two hands to coordinate")]
    public List<streamData> objectsToStream;
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Only 1 server on the network")]
    public bool isServer = false;

    [Header("Safe after a few seconds.")]
    public bool isStreaming = false;

    //make this unique to the device (Unity's dev id is ok, or ip address might work)
    //set first thing in start.
    private string id;

    //object streams have the following ID and their source looks like <objectIdentifier><streamName>
    private string objectStreamID = "id_pos_rot_scale";
    private string objectIdentifier = "object_";
    liblsl.StreamInfo lslObjectInfo;
    liblsl.StreamOutlet lslObjectOutlet;
    liblsl.StreamInlet lslObjectInlet;

    //head/hand streams have the following ID and their source looks like <objectIdentifier><streamName>
    private string headStreamID = "head_pos_rot_scale";
    private string headIdentifier = "head_";
    liblsl.StreamInfo lslHeadInfo;
    liblsl.StreamOutlet lslHeadOutlet;
    List<liblsl.StreamInlet> lslHeadInlet = new List<liblsl.StreamInlet>();
    private string handStreamID = "hand_pos_rot_scale";
    private string handIdentifier = "hand_";
    liblsl.StreamInfo lslHandInfo;
    liblsl.StreamOutlet lslHandOutlet;
    List<liblsl.StreamInlet> lslHandInlet = new List<liblsl.StreamInlet>();

    //max number of objects read each frame. may tweak lower.
    private int maxBufLen = 100;

    private int objectDataSize = 10;
    // Start is called before the first frame update
    void Start()
    {
        id = SystemInfo.deviceUniqueIdentifier;
        lslHeadInfo = new liblsl.StreamInfo("StreamObject", headStreamID, objectDataSize, 0,
            liblsl.channel_format_t.cf_float32, headIdentifier + id);
        lslHeadOutlet = new liblsl.StreamOutlet(lslHeadInfo);
    }

    private Coroutine clientReader = null;
    public void StartClient()
    {
        if (clientReader == null)
        {
            Debug.Log("gh1");
            ScanForObjectStream();
            if (lslObjectInlet != null)
            {
                clientReader = StartCoroutine(HandleObjectSamples());
            } else
            {
                Debug.LogWarning("Warning: could not find an object stream ... check the server and try again.");
            }
        } else
        {
            Debug.LogWarning("Warning: stream already started ... ignoring and continuing");
        }
    }

    /// <summary>
    /// client only read
    /// </summary>
    /// <returns>coroutine</returns>
    public IEnumerator HandleObjectSamples()
    {
        yield return new WaitForSeconds(1);
        float[] buffer = new float[objectDataSize];
        Vector3 pos = new Vector3();
        Quaternion q = new Quaternion();
        Vector3 rot = new Vector3();
        Vector3 scale = new Vector3();
        double t = 0;
        while (true)
        {
            if (isServer)
            {
                break;
            }

            //non-blocking, but clear everything each frame
            //set maxBufLen to decrease total allowed to be in pull_sample. 
            t = lslObjectInlet.pull_sample(buffer, 0.0);
            if (t > 0)
            {
                pos.Set(buffer[1], buffer[2], buffer[3]);
                rot.Set(buffer[4], buffer[5], buffer[6]);
                q.eulerAngles = rot;
                scale.Set(buffer[7], buffer[8], buffer[9]);
                objectsToStream[(int)(buffer[0] + 0.5f)].gameObject.transform.SetPositionAndRotation(pos, q);
            }
            yield return null;
        }
        yield return null;
    }

    /// <summary>
    /// Add streaming objects at run time.
    /// TODO: This is trickier than it looks. 
    ///       Have to coordinate server and client.
    ///       Prefer adding directly to the list in the editor.
    /// </summary>
    /// <param name="g">Gameobject to add</param>
    public void AddStreamingObject(GameObject g)
    {
        streamData s = new streamData();
        s.gameObject = g;
        s.type = objectsToStream.Count;
        objectsToStream.Add(s);
    }

    /// <summary>
    /// Set this as the server. A checkbox at startup or something would work. Or read from a config file, or etc...
    /// Starts up the object outlet stream
    /// </summary>
    public void SetServer()
    {
        isServer = true;
        if (isServer)
        {
            Debug.Log("Starting device as server.");
            //this stream will have all the objects that are enabled to stream.
            lslObjectInfo = new liblsl.StreamInfo("StreamObjects", objectStreamID, objectDataSize, 0,
                liblsl.channel_format_t.cf_float32, objectIdentifier + id);
            lslObjectOutlet = new liblsl.StreamOutlet(lslObjectInfo);
            Debug.Log("started stream: " + lslObjectOutlet.info().source_id());
        }
    }

    /// <summary>
    /// For the server this starts the objects and the head/hands.
    /// For clients, just the head/hands
    /// </summary>
    /// <param name="s">streaming state</param>
    public void SetStreaming(bool s)
    {
        isStreaming = s;
    }

    private double streamSearchTime = 1.0;//s
    void ScanForObjectStream()
    {
        if(isServer)
        {
            Debug.LogError("Error: only the clients should receive the object location stream ... continuing.");
            return;
        }
        Debug.Log("Searching for object streams.");
        liblsl.StreamInfo[] allInlets = liblsl.resolve_streams(streamSearchTime);
        Debug.Log("Done searching for streams. Found " + allInlets.Length + " streams.");
        foreach(var s in allInlets)
        {
            string streamType = s.type();
            string streamSourceId = s.source_id();
            Debug.Log("   stream id: " + streamSourceId);
            if (streamSourceId.StartsWith("object_"))
            {
                lslObjectInlet = new liblsl.StreamInlet(s);
            }
        }
    }

    private float[] objectBuffer = new float[10];
    void Update()
    {
        if (isStreaming)
        {
            if (isServer)
            {
                int i = 0;
                foreach (var t in objectsToStream)
                {
                    objectBuffer[0] = t.type;
                    objectBuffer[1] = t.gameObject.transform.position.x;
                    objectBuffer[2] = t.gameObject.transform.position.y;
                    objectBuffer[3] = t.gameObject.transform.position.z;
                    objectBuffer[4] = t.gameObject.transform.eulerAngles.x;
                    objectBuffer[5] = t.gameObject.transform.eulerAngles.y;
                    objectBuffer[6] = t.gameObject.transform.eulerAngles.z;
                    objectBuffer[7] = t.gameObject.transform.localScale.x;
                    objectBuffer[8] = t.gameObject.transform.localScale.y;
                    objectBuffer[9] = t.gameObject.transform.localScale.z;
                    lslObjectOutlet.push_sample(objectBuffer);
                    //Debug.Log("gh1");
                }
                ++i;
            }
        }
    }
}
