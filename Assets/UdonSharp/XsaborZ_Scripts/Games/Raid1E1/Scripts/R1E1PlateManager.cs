
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1PlateManager : UdonSharpBehaviour
{
    [FieldChangeCallback(nameof(PlateDataChanged))]
    protected DataDictionary PlateData;

    [FieldChangeCallback(nameof(RowSelected))]
    private int Selected = -1;

    [UdonSynced] private bool SetVisuals;
    [UdonSynced] private int NotcjRow;
    [UdonSynced] private int jRow; 
    
    
    void Start()
    {
        
    }

    public DataDictionary PlateDataChanged
    {
        set
        {
            PlateData = value;
            if (PlateData.ContainsKey("Reset"))
            {
                ResetMaterials();
                return;
            }

            Debug.Log(transform.name + " Data Recieved");
            jRow = PlateData["ijRow"].Int;
            NotcjRow = 6 - (PlateData["icRow"].Int + jRow);

            SetVisuals = true;

            RequestSerialization(); // any code after this line replicate in OnDeserialization

            // This is for visual testing -- replace later
            ResetMaterials();
            SetRowData();

            // Don't replicate further
        }

        get => PlateData; 
    }

    public int RowSelected
    {
        set
        {
            Selected = value;
            if (Selected == -1) return; // general fix for needing to hit the same button twice in a row
            //RequestSerialization(); 
            VerifySelected();
        }
        get => Selected;
    }

    private void VerifySelected()
    {
        if (!Networking.IsMaster) return;

        int _selected = Selected +1;
        string log = transform.name + ": Plate Row Selected: " + _selected;
        bool pass = false;
        if (_selected == jRow)
        {
            log += ":  Pass";
            pass = true;
        }
        else
            log += ":  Fail";

        Debug.Log(log);
        if (pass)
        {
            //if (!Networking.IsMaster) return;
            transform.parent.GetChild(1).transform.GetComponent<UdonBehaviour>().SetProgramVariable("Access", pass);
        }
        Selected = -1;
    }

    public override void OnDeserialization() // only needed for initial setup / material changes
    {
        Debug.Log("Plates Request Recieved: Selected: " + Selected);
        
        if (SetVisuals)
        {
            ResetMaterials();
            SetRowData();
            SetVisuals = false;
            RequestSerialization();
        }
    }




    //visual testing
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

    public void SetRowData()
    {        
        Renderer jRend = transform.GetChild(jRow - 1).GetComponent<Renderer>();
        MaterialPropertyBlock jActive = new MaterialPropertyBlock();
        jRend.GetPropertyBlock(jActive);
        jActive.SetColor("_Color", Color.green);
        jRend.SetPropertyBlock(jActive);

        Renderer cRend = transform.GetChild(NotcjRow - 1).GetComponent<Renderer>();
        MaterialPropertyBlock cActive = new MaterialPropertyBlock();
        cRend.GetPropertyBlock(cActive);
        cActive.SetColor("_Color", Color.green);
        cRend.SetPropertyBlock(cActive);
    }
}
