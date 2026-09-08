namespace Atcbassemblyrecipe.Models
{
    // One row in TBLDBREQUEST - see Database/tbldbrequest.sql for the DDL.
    public class DatabaseChangeRequest
    {
        public long RequestId { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public string TargetTable { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime RequestedDate { get; set; }
        public string Status { get; set; } = RequestStatus.Pending;
        public string ReviewedBy { get; set; } = string.Empty;
        public DateTime? ReviewedDate { get; set; }
        public string ReviewNote { get; set; } = string.Empty;
    }

    // Mirrors ck_tbldbrequest_status.
    public static class RequestStatus
    {
        public const string Pending = "PENDING";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";

        public static readonly string[] All = [Pending, Approved, Rejected];
    }

    // Mirrors ck_tbldbrequest_type. The dropdown on the DB Validation page is
    // built from this list, so the two can never drift apart.
    public static class RequestTypes
    {
        public const string Truncate = "TRUNCATE";
        public const string AddColumn = "ADD COLUMN";
        public const string CreateTable = "CREATE TABLE";
        public const string InsertRow = "INSERT NEW ROW";
        public const string UpdateRow = "UPDATE ROW";
        public const string DeleteRow = "DELETE ROW";

        public static readonly string[] All =
        [
            Truncate,
            AddColumn,
            CreateTable,
            InsertRow,
            UpdateRow,
            DeleteRow
        ];
    }
}
