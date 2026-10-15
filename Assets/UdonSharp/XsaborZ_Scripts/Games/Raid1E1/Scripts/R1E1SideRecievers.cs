
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1SideRecievers : UdonSharpBehaviour
{
    // This is just used for transfering Data to the plate and Rings
    // current challenge has passed or failed

    [FieldChangeCallback(nameof(CCDataChanged))]
    protected DataDictionary CCData;
    
    void Start()
    {
    }

    private DataDictionary CCDataChanged
    {
        set
        {
            if (!Networking.IsMaster) return; // only master should be changing these
            CCData = value;
            if (CCData.ContainsKey("Reset"))
            {
                transform.GetChild(0).transform.GetComponent<UdonBehaviour>().SetProgramVariable("PlateData", CCData);
                transform.GetChild(1).transform.GetComponent<UdonBehaviour>().SetProgramVariable("RingsData", CCData);
                return;
            }
            string Log = transform.name + " DataRecieved \n";
            Log += "Code:  Blessing: " + CCData["bBlessing"].Boolean + " Column: " + CCData["icColumn"].Int + " Row: " + CCData["icRow"].Int + "\n";
            Log += "Jump:  Blessing: " + CCData["bBlessing"].Boolean + " Column: " + CCData["ijColumn"].Int + " Row: " + CCData["ijRow"].Int + "\n";

            DataDictionary PlateData = new DataDictionary();
            PlateData.Add("bBlessing", CCData["bBlessing"].Boolean);
            PlateData.Add("icRow", CCData["icRow"].Int);
            PlateData.Add("ijRow", CCData["ijRow"].Int);

            Log += "\nPlate Data: Blessing: " + PlateData["bBlessing"].Boolean + " Code Row: " + PlateData["icRow"].Int + " Jump Row: " + PlateData["ijRow"].Int;
            
            Debug.Log(Log);

            // Send Data to Plate
            transform.GetChild(0).transform.GetComponent<UdonBehaviour>().SetProgramVariable("PlateData", PlateData);
            // Send Data to Rings
            transform.GetChild(1).transform.GetComponent<UdonBehaviour>().SetProgramVariable("RingsData", CCData);
            // Do stuff here
        }
        get => CCData;
    }
}
