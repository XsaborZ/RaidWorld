using System.Security.Cryptography;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

public class R1E1Manager : UdonSharpBehaviour
{
    protected DataList EnCodeList;
    protected int LvDifficulty;
    protected int currentSet;

    [FieldChangeCallback(nameof(SidePass))]
    protected bool Pass = false;
    [FieldChangeCallback(nameof(SideFailed))]
    protected bool Failed = false;
    protected int iSidesPassed = 0;
    protected int iSetCompleted = 0;


    private void Start()
    {
        EnCodeList = new DataList();
        LvDifficulty = 0;
    }

    public void OnEventSender() // used only for testing
    {
        if (!Networking.IsMaster) return;
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnStart");
    }

    public void EnStart()
    {
        if (!Networking.IsMaster) return;
        LvDifficulty++;

        if (LvDifficulty > 6)
            LvDifficulty = 1;

        EnInitiate();
    }

    // this is called at the start of any difficulty
    private void EnInitiate() 
    {
        if (!Networking.IsMaster) return;

        EnCodeList.Clear();
        iSidesPassed = 0;
        iSetCompleted = 0;
        currentSet = 0;

        // DataDictionary Creation
        EnDictCreation();

        // Send Data
        SetDicts(currentSet); // will always start at 0 --- will be called again when needed for next round (not difficulty level)

        // Logger
        Logger();
    }

    private void EnDictCreation()
    {
        bool bBlessing = Random.Range(0,2) == 0;

        for (int i = 0; i < LvDifficulty; i++)
        {
            int cColumn = Random.Range(1, 4);
            int cRow = Random.Range(1, 4);
            int jColumn = Random.Range(1, 4);
            int jRow;
            do
            {
                jRow = Random.Range(1, 4); 
            } while (jRow == cRow);

            DataDictionary EnCodeDataDict = new DataDictionary();
            EnCodeDataDict.Add("bBlessing", bBlessing);
            EnCodeDataDict.Add("icColumn", cColumn);
            EnCodeDataDict.Add("icRow", cRow);
            EnCodeDataDict.Add("ijColumn", jColumn);
            EnCodeDataDict.Add("ijRow", jRow);

            EnCodeList.Add(EnCodeDataDict);
            bBlessing = !bBlessing; // Swaps for next
        }
    }

    private void SetDicts(int DaPos)
    {
        //int cCount = 0; //tester number -- currently only able to do set 1 and 2
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).transform.GetComponent<UdonBehaviour>().SetProgramVariable("CCData", EnCodeList[currentSet].DataDictionary);
            //DaPos++;
            currentSet++;
            Debug.Log("Manager Child: " + i + "\nSet: " + currentSet + "\nCodeCount: " + EnCodeList.Count);
            if (EnCodeList.Count == currentSet)
            {
                if (((currentSet - 1) % 2) == 1) return; // checks to see if current set is even -- if true dont reset this side
                Debug.Log("Reseting Side");
                DataDictionary temp = new DataDictionary();
                temp.Add("Reset", true);
                transform.GetChild(1).transform.GetComponent<UdonBehaviour>().SetProgramVariable("CCData", temp); // sending blank to second child
                return;
            }
        }
    }

    private void GetNextSet()
    {
        if (iSetCompleted >= EnCodeList.Count)
        {
            LvDifficulty++;
            EnInitiate();
            return;
        }
        iSidesPassed = 0;
        SetDicts(currentSet);
    }

    private bool SidePass
    {
        set
        {
            Pass = value;
            Debug.Log("Side Passed");
            iSidesPassed++;
            iSetCompleted++;
            bool isOddLv = ((LvDifficulty - 1) % 2) == 0;
            //if (isOddLv)
            //    Debug.Log("Current level is Odd: " + isOddLv);
            if (iSidesPassed == 2 || (isOddLv && (iSetCompleted == EnCodeList.Count))) // Set is only odd if on final - can only pass if both lv is odd and on final set
            {
                iSidesPassed = 0;
                Pass = false;
                // Get Next Set
                Debug.Log("Sets Completed: " + iSetCompleted);
                GetNextSet();
            }
            Pass = false;
        }
        get => Pass;
    }

    private bool SideFailed
    {
        set
        {
            Failed = value;
            Debug.Log("Encounter Failed. Reseting Lv: " + LvDifficulty);
            EnInitiate(); // Reset
            Failed = false;
        }
        get => SideFailed;
    }

    private void Logger()
    {
        string Log = "Encounter 1 Variables: Difficulty: " + LvDifficulty + "\n";
        for (int i = 0; i < EnCodeList.Count; i++)
        {
            DataDictionary dict = EnCodeList[i].DataDictionary;
            Log += "Code: " + (i+1) + " Blessing: " + dict["bBlessing"].Boolean + " Column: " + dict["icColumn"].Int + " Row: " + dict["icRow"].Int + "\n";
            Log += "Jump: " + (i+1) + " Blessing: " + dict["bBlessing"].Boolean + " Column: " + dict["ijColumn"].Int + " Row: " + dict["ijRow"].Int + "\n";
        }
        Debug.Log(Log);
    }
}