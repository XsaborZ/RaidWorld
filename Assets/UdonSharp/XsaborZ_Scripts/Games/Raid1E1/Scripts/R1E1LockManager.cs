
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1LockManager : UdonSharpBehaviour
{
    [FieldChangeCallback(nameof(LockListChange))]
    protected DataList LockList;

    protected DataList LList;
    protected DataList DList;

    void Start()
    {
        LList = new DataList();
        DList = new DataList();
    }

    private DataList LockListChange
    {
        set
        {
            LockList = value;
            Logger();

            CreateDLLists();

        }
        get => LockList;
    }

    private void CreateDLLists()
    {
        LList.Clear();
        DList.Clear();

        // Separate list into designated blessing types
        for (int i = 0; i < LockList.Count; i++)
        {
            DataDictionary pos = LockList[i].DataDictionary;
            if (pos["bBlessing"].Boolean)
                DList.Add(pos); 
            else
                LList.Add(pos);
        }

        DLlistsLogger();


    }

    private void DLlistsLogger()
    {
        string log = "DLLists Created:" + "\n";
        if (LList.Count > 0)
        {
            for (int i = 0; i < LList.Count; i++)
            {
                int codePos = LList[i].DataDictionary["icColumn"].Int + (3 * (LList[i].DataDictionary["icRow"].Int - 1));
                log += "Position: " + codePos + "  Blessing:" + LList[i].DataDictionary["bBlessing"].Boolean + "\n";
            }
        }
        else
            log += "LList is Empty \n";

        if (DList.Count > 0)
        {
            for (int i = 0; i < DList.Count; i++)
            {
                int codePos = DList[i].DataDictionary["icColumn"].Int + (3 * (DList[i].DataDictionary["icRow"].Int - 1));
                log += "Position: " + codePos + "  Blessing:" + DList[i].DataDictionary["bBlessing"].Boolean + "\n";
            }
        }
        else
            log += "DList is Empty \n";

        Debug.Log(log);
    }
    private void Logger()
    {
        string Log = "PadLock List" + "\n"; 
        for (int i = 0; i < LockList.Count; i++)
        {
            DataDictionary dict = LockList[i].DataDictionary;
            Log += "Code: " + (i + 1) + " Blessing: " + dict["bBlessing"].Boolean + " Column: " + dict["icColumn"].Int + " Row: " + dict["icRow"].Int + "\n";
            //Log += "Jump: " + (i + 1) + " Blessing: " + dict["bBlessing"].Boolean + " Column: " + dict["ijColumn"].Int + " Row: " + dict["ijRow"].Int + "\n";
        }
        Debug.Log(Log);
    }
}
