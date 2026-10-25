using System.Text;
using UnityEngine;
namespace MotelGame.Service
{
    public sealed class StaffTaskBoard : MonoBehaviour
    {
        [SerializeField] ReceptionDesk reception;
        [SerializeField] GuestServiceRequests requests;
        [SerializeField] NightShiftController shift;
        [SerializeField] TextMesh label;
        float nextUpdate;
        public string DisplayText=>label.text;
        void Update()
        {
            if(Time.unscaledTime<nextUpdate)return;nextUpdate=Time.unscaledTime+.2f;
            var text=new StringBuilder("STAFF / NIGHT ").Append(shift.ClockText).Append('\n');
            if(!shift.IsRunning)text.Append(shift.State.Phase).Append('\n');
            foreach(var room in reception.Rooms)
                text.Append(room.RoomNumber).Append(" — ").Append(room.State.Status==RoomStatus.Dirty?"Cleaning required":room.State.Status.ToString()).Append('\n');
            text.Append("SERVICE\n");
            if(requests.Room==null)text.Append("No pending call");
            else text.Append(requests.Room.RoomNumber).Append(" — ").Append(requests.RequestName).Append('\n').Append(
                requests.Phase==GuestRequestPhase.Ringing?"Answer Reception phone":requests.Phase==GuestRequestPhase.Completed?"Completed":
                requests.State.Kind==GuestRequestKind.CleanTowel?"Collect / deliver towel":requests.State.RepairStep==0?"Connect cable → reset TV":"Reset TV tuning");
            label.text=text.ToString();
        }
        public bool ConfigurationValid()=>reception!=null&&requests!=null&&shift!=null&&label!=null;
    }
}
