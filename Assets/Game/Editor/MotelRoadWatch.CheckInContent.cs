using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MotelGame.Registration;

namespace MotelGame.Editor
{
    public static partial class MotelRoadWatch
    {
        static GuestCase[] CheckInCases()
        {
            string[] assets={"01_Wade_Mercer","02_Ellis_Boone","03_Jonah_Voss","05_Sam_Ortiz","06_Martin_Cole","07_Roy_Keller"};
            var cases=assets.Select(n=>AssetDatabase.LoadAssetAtPath<GuestCase>(Content+"/Cases/"+n+".asset")).ToArray();
            string[] birthdays={"14 MAR 1967","06 JUL 1972","23 NOV 1968","02 FEB 1975","18 SEP 1961","30 APR 1970"};
            for(int i=0;i<cases.Length;i++)
            {
                var c=cases[i];var name=c.guestName.Split(' ');c.firstName=name[0];c.lastName=name[1];c.dateOfBirth=birthdays[i];
                c.bookingFirstName=c.firstName;c.bookingLastName=c.lastName;c.checkInDate="27 OCT 2002";c.expectedGuests=1;c.requestedRoom=(101+i).ToString();
                c.hasReservation=i!=4;c.hasReport=i<3;c.claimedPlate=c.actualPlate;c.countQuestion=c.countAnswer=c.noteQuestion=c.noteAnswer=c.holderQuestion=c.holderAnswer="";
                c.reservationEntry=c.hasReservation?c.guestName+" / tonight / one room":"Walk-in";
                c.reservationAnswer=c.hasReservation?"Yes. I have a booking for tonight.":"I didn't book. Is there a room available?";
            }
            var a=cases[0];a.bookingNote="Late arrival. Wildlife depot appointment at 06:00. Please keep room until midnight.";
            a.noteQuestion="Your note mentions the wildlife depot at six. What happened?";
            a.noteAnswer="I hit a deer on Pine Cut. Dispatch asked me to bring it in, but the depot is closed until morning. I have it covered in the trunk.";
            a.authorReasoning="Human. Blood/open trunk is suspicious; booking, note answer, report dispatch call and visible antler agree. Admit: ordinary stay; refuse: lost stay.";
            a=cases[1];a.expectedGuests=2;a.bookingNote="Ellis + coworker Dale. Overnight fuel-pump repair at Marlow Fuel.";
            a.countQuestion="Your booking is for two people. Is someone else joining you?";a.countAnswer="Dale got called to another station. It's just me tonight. We booked before his schedule changed.";
            a.noteQuestion="Were you repairing a pump at Marlow Fuel tonight?";a.noteAnswer="Pump three. The attendant left the service key in the red box. My tools are still in the van.";
            a.authorReasoning="Human. Expected count differs for an explained work change. Report shows tools at pump three, consistent with note and answer. Admit: normal stay; refuse: lost stay.";
            a=cases[2];a.bookingNote="Coming from Eastbank via Alder Bridge. Expected around 22:15.";
            a.noteQuestion="Your booking says Alder Bridge. Did you use that route tonight?";a.noteAnswer="Yes. Straight across at ten. The road was dry and clear; there was no detour.";
            a.authorReasoning="Mimic. Booking-question answer claims crossing at 22:00; report/photo show this car and driver at closed washed-out bridge at 22:05, closed since 18:00. Doubles down in report question. Admit: existing delayed infiltration/hunt; refuse: leaves safely.";
            a=cases[3];a.bookingFirstName="Samuel";a.bookingNote="Hospital supply run. Late arrival after final delivery.";
            a.holderQuestion="The booking says Samuel Ortiz; your license says Sam. Is this yours?";a.holderAnswer="Yes. Dispatch books me as Samuel on the company account. My license uses Sam. The last name and hospital job should match.";
            a.noteQuestion="Are you coming from the hospital delivery?";a.noteAnswer="County Memorial. The final supply drop ran late. The gray van outside still has the empty delivery crates.";
            a.authorReasoning="Human, no report. Explained booking-name variant (Samuel/Sam) agrees with surname and work note. A name mismatch alone is not a Mimic rule. Admit: normal stay; refuse: lost stay.";
            a=cases[4];a.bookingFirstName=a.bookingLastName=a.checkInDate=a.requestedRoom=a.bookingNote="";
            a.authorReasoning="Human walk-in, no report. Automatic license, no reservation, truthful parking. No contradictory clue. Admit to any ready room: normal stay; refuse: lost stay.";
            a=cases[5];a.bookingFirstName="Ray";a.expectedGuests=2;a.bookingNote="Roy Keller + Arthur. Mill Road boiler installation. Arriving together.";
            a.holderQuestion="The booking holder is Ray, but your license says Roy. Which name is correct?";a.holderAnswer="Ray. My license says Ray Keller. Read it again; it's the same name.";
            a.countQuestion="Your reservation lists Arthur as a second guest. Is he joining you?";a.countAnswer="There is no Arthur. I always work alone. I'm a night security guard, not a boiler installer.";
            a.noteQuestion="The note says a boiler installation on Mill Road. Is that your job?";a.noteAnswer="No installation. Night security. I wrote that booking myself and I never mentioned a coworker.";
            a.drivingAnswer=a.vehicleDescription="Black sedan. I parked outside.";a.plateAnswer="WQN-701.";a.followUpQuestion=a.followUpAnswer="";
            a.authorReasoning="Mimic without report. Must ask booking fields: denies the visibly printed license name, denies his own note and coworker, changes profession. Human Sam has an explained name variation, Ellis an explained count change, so mismatch alone is not the rule. Admit: existing delayed infiltration/hunt; refuse: leaves safely.";
            foreach(var c in cases)EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();return cases;
        }
    }
}
