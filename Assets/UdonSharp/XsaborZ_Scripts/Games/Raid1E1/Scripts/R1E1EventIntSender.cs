
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1EventIntSender : UdonSharpBehaviour
{
    void Start()
    {
        
    }

    public override void Interact()
    {
        int position = 0;
        for (int i = 0; i < transform.parent.childCount; i++)
        {
            if (transform.parent.GetChild(i).name == transform.name)
            {
                position = i; 
                break;
            }
        }
        transform.parent.transform.GetComponent<UdonBehaviour>().SetProgramVariable("Selected", position);
    }
}
