using System;
using UnityEngine;

namespace MotelGame.Registration
{
    public enum RegistrationQuestion : byte { Name, Reservation, Driving, Plate, Origin, Purpose, FollowUp, GuestCount, BookingNote, ParkingSpace, BookingName }
    public enum RegistrationDecision : byte { Pending, Admitted, Refused, Missing }
    public enum GuestVehicleKind : byte { Sedan, Van }

    [Serializable]
    public sealed class RoadWatchReport
    {
        public string headline, location, eventTime, source;
        [TextArea] public string description;
        public Texture2D photograph;
    }

    [CreateAssetMenu(menuName="Motel/Guest registration case")]
    public sealed class GuestCase : ScriptableObject
    {
        public string guestName;
        public string firstName, lastName, dateOfBirth;
        public Texture2D portrait;
        public string bookingFirstName, bookingLastName, checkInDate, requestedRoom;
        [Range(1,4)] public int expectedGuests = 1;
        [TextArea] public string bookingNote;
        public string countQuestion, noteQuestion, holderQuestion;
        [TextArea] public string countAnswer, noteAnswer, holderAnswer;
        public string Question(RegistrationQuestion q) => q switch {
            RegistrationQuestion.GuestCount => countQuestion,
            RegistrationQuestion.BookingNote => noteQuestion,
            RegistrationQuestion.BookingName => holderQuestion,
            RegistrationQuestion.ParkingSpace => "Where did you park?",
            RegistrationQuestion.FollowUp => followUpQuestion,
            _ => ""
        };
        public bool Supports(RegistrationQuestion q) => q==RegistrationQuestion.ParkingSpace ||
            (hasReservation && (q==RegistrationQuestion.GuestCount || q==RegistrationQuestion.BookingNote || q==RegistrationQuestion.BookingName) && !string.IsNullOrWhiteSpace(Question(q)));
        [Range(0,2)] public int appearance;
        public Color clothing = Color.gray;
        public GuestVehicleKind vehicleKind;
        public Color vehicleColor = Color.gray;
        public string actualPlate, vehicleDescription;
        public bool hasReservation;
        public string reservationEntry;
        [TextArea] public string nameAnswer, reservationAnswer, drivingAnswer, plateAnswer, originAnswer, purposeAnswer;
        public string claimedPlate;
        public bool hasReport;
        public RoadWatchReport report;
        public string followUpQuestion;
        [TextArea] public string followUpAnswer;
        [Tooltip("Author/server data only. Never used by registration UI or sent as a verdict.")]
        public bool disguisedMimic;
        [Min(10)] public float disguiseSeconds = 45;
        [TextArea] public string authorReasoning;
        public string Answer(RegistrationQuestion q) => q switch {
            RegistrationQuestion.GuestCount=>countAnswer, RegistrationQuestion.BookingNote=>noteAnswer,
            RegistrationQuestion.BookingName=>holderAnswer,
            RegistrationQuestion.Name=>nameAnswer, RegistrationQuestion.Reservation=>reservationAnswer,
            RegistrationQuestion.Driving=>drivingAnswer, RegistrationQuestion.Plate=>plateAnswer,
            RegistrationQuestion.Origin=>originAnswer, RegistrationQuestion.Purpose=>purposeAnswer,
            _=>followUpAnswer
        };
        public bool Valid(out string error)
        {
            error="";
            if(string.IsNullOrWhiteSpace(firstName)||string.IsNullOrWhiteSpace(lastName)||string.IsNullOrWhiteSpace(dateOfBirth)||portrait==null)error="Missing license / portrait";
            if(hasReservation&&(string.IsNullOrWhiteSpace(bookingFirstName)||string.IsNullOrWhiteSpace(bookingLastName)||string.IsNullOrWhiteSpace(checkInDate)||expectedGuests<1||expectedGuests>4))error="Incomplete reservation";
            foreach(var q in new[]{RegistrationQuestion.GuestCount,RegistrationQuestion.BookingNote,RegistrationQuestion.BookingName})
                if(Supports(q)&&(string.IsNullOrWhiteSpace(Answer(q))||System.Text.Encoding.UTF8.GetByteCount(Question(q))>120||System.Text.Encoding.UTF8.GetByteCount(Answer(q))>480))error="Invalid booking dialogue";
            foreach(string text in new[]{firstName,lastName,dateOfBirth,bookingFirstName,bookingLastName,checkInDate,requestedRoom})
                if(System.Text.Encoding.UTF8.GetByteCount(text??"")>28)error="Identity field exceeds network limit";
            if(System.Text.Encoding.UTF8.GetByteCount(bookingNote??"")>240)error="Booking note too long";
            if(string.IsNullOrWhiteSpace(guestName)||RegistrationDesk.NormalizePlate(actualPlate).Length<3||RegistrationDesk.NormalizePlate(claimedPlate).Length<3)error="Missing identity / plate";
            for(int i=0;i<6;i++)if(string.IsNullOrWhiteSpace(Answer((RegistrationQuestion)i))||System.Text.Encoding.UTF8.GetByteCount(Answer((RegistrationQuestion)i))>480)error="Missing / oversized answer";
            if(System.Text.Encoding.UTF8.GetByteCount(guestName??"")>55||(actualPlate??"").Length>20||(claimedPlate??"").Length>20||System.Text.Encoding.UTF8.GetByteCount(reservationEntry??"")>110||System.Text.Encoding.UTF8.GetByteCount(followUpQuestion??"")>110)error="Record too long";
            if(hasReport&&(report==null||report.photograph==null||string.IsNullOrWhiteSpace(report.headline)||string.IsNullOrWhiteSpace(report.description)||string.IsNullOrWhiteSpace(report.source)||string.IsNullOrWhiteSpace(report.eventTime)))error="Incomplete RoadWatch report";
            if(!string.IsNullOrEmpty(followUpQuestion)&&(string.IsNullOrEmpty(followUpAnswer)||System.Text.Encoding.UTF8.GetByteCount(followUpAnswer)>480))error="Missing follow-up answer";
            if(string.IsNullOrWhiteSpace(authorReasoning)||disguiseSeconds<10||float.IsNaN(disguiseSeconds)||float.IsInfinity(disguiseSeconds))error="Missing evidence / invalid disguise delay";
            return error.Length==0;
        }
    }
}
