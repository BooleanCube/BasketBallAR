using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Throwable : MonoBehaviour
{
    public float velocityBoost = 1000f;
    public Rigidbody rigidBody;
    
    private Vector3 curPosition, oldPosition;
    
    // Start is called before the first frame update
    void Start()
    {
        curPosition = this.gameObject.transform.position;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        oldPosition = curPosition;
        curPosition = this.gameObject.transform.position;
    }

    public void PerformThrow()
    {
        Vector3 direction = curPosition - oldPosition;
        rigidBody.AddForce(direction * velocityBoost);
    }
}
