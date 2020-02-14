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

        public streamData(GameObject o, int t)
        {
            gameObject = o;
            type = t;
        }
    };
    //put all the objects to stream here. The server owns all the objects, each client owns the head/hands.
    //one head and two hands hacked in
    [Header("server coordinates these objects")]
    public List<streamData> objectsToStream;

    [Header("Only 1 server on the network")]
    public bool isServer = false;

    [Header("head and two hands to coordinate")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Safe after a few seconds.")]
    public bool isStreaming = false;

    [Header("Render clients as these.")]
    public List<GameObject> headRendersObjects;
    public List<GameObject> leftHandRendersObjects;
    public List<GameObject> rightHandRendersObjects;
    private int lastRenderedClient = 0;

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
    //hackish if someone with more than two hands or one head shows up
    private string headStreamID = "head_pos_rot_scale";
    private string headIdentifier = "head_";
    liblsl.StreamInfo lslHeadInfo;
    liblsl.StreamOutlet lslHeadOutlet;
    List<liblsl.StreamInlet> lslHeadInlet = new List<liblsl.StreamInlet>();

    private string leftHandStreamID = "left_hand_pos_rot_scale";
    private string leftHandIdentifier = "left_hand_";
    liblsl.StreamInfo lslLeftHandInfo;
    liblsl.StreamOutlet lslLeftHandOutlet;
    List<liblsl.StreamInlet> lslLeftHandInlet = new List<liblsl.StreamInlet>();

    private string rightHandStreamID = "right_hand_pos_rot_scale";
    private string rightHandIdentifier = "right_hand_";
    liblsl.StreamInfo lslRightHandInfo;
    liblsl.StreamOutlet lslRightHandOutlet;
    List<liblsl.StreamInlet> lslRightHandInlet = new List<liblsl.StreamInlet>();

    private Dictionary<string, GameObject> heads = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> leftHands = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> rightHands = new Dictionary<string, GameObject>();

    //max number of objects read each frame. may tweak lower.
    private int maxBufLen = 100;

    private int objectDataSize = 10;
    private int headhandDataSize = 6;

    public GameObject testCube;

    // Start is called before the first frame update
    void Start()
    {
        objectsToStream = new List<streamData>();
        int testSize = 100;
        for(int i = 0; i < testSize; ++i)
        {
            var ins = Instantiate(testCube, 100.0f*Random.insideUnitSphere, Random.rotationUniform);
            objectsToStream.Add(new streamData(ins, i));
        }

        id = SystemInfo.deviceUniqueIdentifier;
        objectBuffer = new float[objectDataSize];
        headhandBuffer = new float[headhandDataSize];

        //only needed for testing on single device (same server and client)
        string letters = "qwertyuiopasdfghjklzxcvbnm";
        for(int i = 0; i < 10; ++ i) { id += letters[Random.Range(0, letters.Length)]; }

        lslHeadInfo = new liblsl.StreamInfo("StreamObject", headStreamID, headhandDataSize, 0,
            liblsl.channel_format_t.cf_float32, headIdentifier + id);
        lslHeadOutlet = new liblsl.StreamOutlet(lslHeadInfo, 0, maxBufLen);
        lslLeftHandInfo = new liblsl.StreamInfo("StreamObject", leftHandStreamID, headhandDataSize, 0,
            liblsl.channel_format_t.cf_float32, leftHandIdentifier + id);
        lslLeftHandOutlet = new liblsl.StreamOutlet(lslLeftHandInfo, 0, maxBufLen);
        lslRightHandInfo = new liblsl.StreamInfo("StreamObject", rightHandStreamID, headhandDataSize, 0,
            liblsl.channel_format_t.cf_float32, rightHandIdentifier + id);
        lslRightHandOutlet = new liblsl.StreamOutlet(lslRightHandInfo, 0, maxBufLen);

        StartCoroutine(HandleEyeHandSamples());
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
            while (t > 0)
            {
                pos.Set(buffer[1], buffer[2], buffer[3]);
                rot.Set(buffer[4], buffer[5], buffer[6]);
                q.eulerAngles = rot;
                scale.Set(buffer[7], buffer[8], buffer[9]);
                objectsToStream[(int)(buffer[0] + 0.5f)].gameObject.transform.SetPositionAndRotation(pos, q);
                t = lslObjectInlet.pull_sample(buffer, 0.0);
            }
            yield return null;
        }
        yield return null;
    }


    public IEnumerator HandleEyeHandSamples()
    {
        //mostly for luck
        yield return new WaitForSeconds(1);
        float[] buffer = new float[headhandDataSize];
        Vector3 pos = new Vector3();
        Quaternion q = new Quaternion();
        Vector3 rot = new Vector3();
        double t = 0;
        while (true)
        {
            //usual hack here for two hands and one head (venusians can write their own VR:)
            //non-blocking, but clear everything each frame
            //set maxBufLen to decrease total allowed to be in pull_sample. 
            foreach (var h in lslHeadInlet)
            {
                t = h.pull_sample(buffer, 0.0);
                while (t > 0)
                {
                    pos.Set(buffer[0], buffer[1], buffer[2]);
                    rot.Set(buffer[3], buffer[4], buffer[5]);
                    q.eulerAngles = rot;
                    heads[h.info().source_id()].transform.SetPositionAndRotation(pos, q);
                    t = h.pull_sample(buffer, 0.0);
                }
            }
            foreach (var h in lslLeftHandInlet)
            {
                t = h.pull_sample(buffer, 0.0);
                while (t > 0)
                {
                    pos.Set(buffer[0], buffer[1], buffer[2]);
                    rot.Set(buffer[3], buffer[4], buffer[5]);
                    q.eulerAngles = rot;
                    leftHands[h.info().source_id()].transform.SetPositionAndRotation(pos, q);
                    t = h.pull_sample(buffer, 0.0);
                }
            }
            foreach (var h in lslRightHandInlet)
            {
                t = h.pull_sample(buffer, 0.0);
                while (t > 0)
                {
                    pos.Set(buffer[0], buffer[1], buffer[2]);
                    rot.Set(buffer[3], buffer[4], buffer[5]);
                    q.eulerAngles = rot;
                    rightHands[h.info().source_id()].transform.SetPositionAndRotation(pos, q);
                    t = h.pull_sample(buffer, 0.0);
                }
            }
            yield return null;
        }
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
            lslObjectOutlet = new liblsl.StreamOutlet(lslObjectInfo, 0, maxBufLen);
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

    /// <summary>
    /// Scan for players, each of which should be sending data from VR tracking.
    /// Does not include itself or anyone already added.
    /// </summary>
    public void ScanForPlayers()
    {
        Debug.Log("Searching for players.");
        liblsl.StreamInfo[] allInlets = liblsl.resolve_streams(streamSearchTime);
        Debug.Log("Done searching for players. Found " + allInlets.Length + " streams.");
        foreach (var s in allInlets)
        {
            string streamType = s.type();
            string streamSourceId = s.source_id();
            Debug.Log("   stream id: " + streamSourceId);
            if (!IsMe(streamSourceId) && !AlreadyFoundPlayer(streamSourceId))
            {
                if (streamSourceId.StartsWith(headIdentifier))
                {
                    lslHeadInlet.Add(new liblsl.StreamInlet(s));
                    heads.Add(streamSourceId, Instantiate(headRendersObjects[lastRenderedClient % headRendersObjects.Count]));
                } else if (streamSourceId.StartsWith(leftHandIdentifier))
                {
                    lslLeftHandInlet.Add(new liblsl.StreamInlet(s));
                    leftHands.Add(streamSourceId, Instantiate(leftHandRendersObjects[lastRenderedClient % leftHandRendersObjects.Count]));
                }
                else if (streamSourceId.StartsWith(rightHandIdentifier))
                {
                    lslRightHandInlet.Add(new liblsl.StreamInlet(s));
                    rightHands.Add(streamSourceId, Instantiate(rightHandRendersObjects[lastRenderedClient % rightHandRendersObjects.Count]));
                }
                //Just want to cycle through them randomly-ish
                ++lastRenderedClient;
            }
        }
    }

    private bool IsMe(string s)
    {
        return s.EndsWith(id);
    } 

    /// <summary>
    /// check each of heads and hands. Not real fast, or meant to be called often.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private bool AlreadyFoundPlayer(string s)
    {
        foreach (var inlet in lslHeadInlet)
        {
            if (inlet.info().source_id() == s) { return true; }
        }
        foreach (var inlet in lslLeftHandInlet)
        {
            if (inlet.info().source_id() == s) { return true; }
        }
        foreach (var inlet in lslRightHandInlet)
        {
            if (inlet.info().source_id() == s) { return true; }
        }
        return false;
    }

    /// <summary>
    /// On the client: scan for an object stream coming from the server.
    /// Not real safe to call this multiple times.
    /// Server crash is probably bad.
    /// </summary>
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
            if (streamSourceId.StartsWith(objectIdentifier))
            {
                lslObjectInlet = new liblsl.StreamInlet(s);
            }
        }
    }


    private float[] objectBuffer;
    private float[] headhandBuffer;
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

            //clients stream/own their heads and hands (hackish, but 6dof and 1head/2hands is not too bad).
            if (head != null)
            {
                headhandBuffer[0] = head.position.x;
                headhandBuffer[1] = head.position.y;
                headhandBuffer[2] = head.position.z;
                headhandBuffer[3] = head.eulerAngles.x;
                headhandBuffer[4] = head.eulerAngles.y;
                headhandBuffer[5] = head.eulerAngles.z;
                lslHeadOutlet.push_sample(headhandBuffer);
            }
            if (leftHand != null)
            {
                headhandBuffer[0] = leftHand.position.x;
                headhandBuffer[1] = leftHand.position.y;
                headhandBuffer[2] = leftHand.position.z;
                headhandBuffer[3] = leftHand.eulerAngles.x;
                headhandBuffer[4] = leftHand.eulerAngles.y;
                headhandBuffer[5] = leftHand.eulerAngles.z;
                lslLeftHandOutlet.push_sample(headhandBuffer);
            }
            if (rightHand != null)
            {
                headhandBuffer[0] = rightHand.position.x;
                headhandBuffer[1] = rightHand.position.y;
                headhandBuffer[2] = rightHand.position.z;
                headhandBuffer[3] = rightHand.eulerAngles.x;
                headhandBuffer[4] = rightHand.eulerAngles.y;
                headhandBuffer[5] = rightHand.eulerAngles.z;
                lslRightHandOutlet.push_sample(headhandBuffer);
            }
        }
    }
}
