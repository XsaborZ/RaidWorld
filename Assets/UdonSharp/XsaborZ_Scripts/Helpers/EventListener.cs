
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class EventListener : UdonSharpBehaviour
{
    bool isSpinning;

    void Start()
    {
        isSpinning = false;   
    }
    public void OnEventSender()
    {
        Response();
    }

    public void Response()
    {
        Debug.Log("Listener has heared and is responding!");
        isSpinning = !isSpinning;
    }
    private void Update()
    {
        if (!isSpinning) return;
        this.transform.Rotate(Vector3.up * 90 * Time.deltaTime, Space.Self);
    }
}
