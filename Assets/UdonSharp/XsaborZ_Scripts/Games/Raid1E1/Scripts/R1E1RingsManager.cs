
using Newtonsoft.Json.Linq;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1RingsManager : UdonSharpBehaviour
{
    [FieldChangeCallback(nameof(RingsDataChanged))]
    protected DataDictionary RingsData;

    [FieldChangeCallback(nameof(RingSelected))]
    protected int Selected = -1;
    [FieldChangeCallback(nameof(RowPassed))]
    [UdonSynced] private bool Access = false;

    [UdonSynced] private int cRing;
    [UdonSynced] private int jRing;
    [UdonSynced] private bool pass;
    [UdonSynced] private bool playerEntered;

    void Start()
    {

    }

    public DataDictionary RingsDataChanged
    {
        set
        {
            RingsData = value;
            if (RingsData.ContainsKey("Reset"))
            {
                ResetMaterials();
                return;
            }

            Debug.Log(transform.name + " Data Recieved");
            cRing = RingsData["icColumn"].Int + (3 * (RingsData["icRow"].Int - 1));
            jRing = RingsData["ijColumn"].Int + (3 * (RingsData["ijRow"].Int - 1));
            playerEntered = false;
            Access = false;

            RequestSerialization(); // any code after this line replicate in OnDeserialization

            // This is for visual testing
            ResetMaterials();
            SetRingData();

            // Don't replicate further
        }

        get => RingsData;
    }
    public int RingSelected
    {
        set
        {
            //Selected = value; // +1
            if (Access)
            {
                Selected = value;
                if (Selected == -1) return;
                VerifySelected();
            }
            //Selected = -1;
        }
        get => Selected;
    } 

    private void VerifySelected()
    {
        int _selected = Selected + 1;
        pass = false;
        if (_selected == jRing)
            pass = true; // not getting hit

        Debug.Log(transform.name + ": Ring Selected: " + _selected + ":  Correct Ring: " +  jRing + ":  Pass: " + pass); 
       
        playerEntered = true;
        // Serialization only needed if passed or failed
        playerEnteredRing();
        RequestSerialization();
        Selected = -1;

    }
    public bool RowPassed // is multiplayer dependant 
    {
        set
        {
            Access = value;
            if (!Access) return;
            RequestSerialization();
            Debug.Log(transform.name + ": Enabling Rings: ");
        }
        get => Access;
    }

    private void playerEnteredRing()
    {
        if (pass)
            transform.parent.transform.parent.transform.GetComponent<UdonBehaviour>().SetProgramVariable("Pass", true);
        else
            transform.parent.transform.parent.transform.GetComponent<UdonBehaviour>().SetProgramVariable("Failed", true);
    }

    public override void OnDeserialization()
    {
        Debug.Log("Rings Request Recieved: Selected: " + Selected + " Access: " + Access);
        //Networking.SetOwner(Networking.Master, gameObject);
        if (Selected < 0 && !Access)
        {
            ResetMaterials();
            SetRingData();
        }
        if(playerEntered)
        {
            playerEnteredRing();
        }

        //else
        //{
        //    VerifySelected();
        //    if (Networking.IsMaster)
        //    {
        //        Selected = -1;
        //        RequestSerialization();
        //    }
        //}
    }

    public void ResetMaterials()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Renderer Rend = transform.GetChild(i).GetComponent<Renderer>();
            MaterialPropertyBlock Deactive = new MaterialPropertyBlock();
            Rend.GetPropertyBlock(Deactive);
            Deactive.SetColor("_Color", Color.blue);
            Rend.SetPropertyBlock(Deactive);
        }
    }

    public void SetRingData()
    {
        Renderer jRend = transform.GetChild(jRing - 1).GetComponent<Renderer>();
        MaterialPropertyBlock jActive = new MaterialPropertyBlock();
        jRend.GetPropertyBlock(jActive);
        jActive.SetColor("_Color", Color.green);
        jRend.SetPropertyBlock(jActive);

        Renderer cRend = transform.GetChild(cRing - 1).GetComponent<Renderer>();
        MaterialPropertyBlock cActive = new MaterialPropertyBlock();
        cRend.GetPropertyBlock(cActive);
        cActive.SetColor("_Color", Color.green);
        cRend.SetPropertyBlock(cActive);
    }
}
