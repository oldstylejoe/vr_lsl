using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestMover : MonoBehaviour
{
    public LSLServer lsl;

    // Start is called before the first frame update
    void Start()
    {

        
    }

    private void Update()
    {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                var p = gameObject.transform.position;
                p.y += 0.1f;
                gameObject.transform.position = p;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                var p = gameObject.transform.position;
                p.y -= 0.1f;
                gameObject.transform.position = p;
            }
    }

}
