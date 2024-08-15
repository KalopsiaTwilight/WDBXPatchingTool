namespace DBXPatching.Core
{
    public class Patch
    {
        public List<AddRecordInstruction> Add { get; set; }
        public List<LookupRecordInstruction> Lookup { get; set; }
        public List<UpdateRecordInstruction> Update { get; set; }
        public Patch()
        {
            Add = [];
            Lookup = [];
            Update = [];
        }
    }

    public class AddRecordInstruction
    {
        public string Filename { get; set; }
        public int? RecordId { get; set; }
        public string? RecordIdReference { get; set; }  
        public List<ColumnData> Record { get; set; }

        public List<ReferenceColumnData> SaveReferences { get; set; }
        public List<GenerateColumnIdData> GenerateIds { get; set; }

        public AddRecordInstruction()
        {
            Filename = string.Empty;
            Record = [];
            SaveReferences = [];
            GenerateIds = [];
        }
    }

    public class LookupRecordInstruction
    {
        public string Filename { get; set; }
        public string Field { get; set; }
        public object? SearchValue { get; set; }
        public List<ReferenceColumnData> SaveReferences { get; set; }
        public bool IgnoreFailure { get; set; }
        public LookupRecordInstruction()
        {
            Filename = string.Empty;
            Field = string.Empty;
            SaveReferences = [];
        }
    }

    public class UpdateRecordInstruction
    {
        public string Filename { get; set; }
        public int RecordId { get; set; }
        public string? Field { get; set; }
        public List<ColumnData> Record { get; set; }

        public UpdateRecordInstruction()
        {
            Filename = string.Empty;
            Record = [];
        }

    }

    public class ReferenceColumnData
    {
        public string Name { get; set; }
        public string? Field { get; set; }

        public ReferenceColumnData()
        {
            Name = string.Empty;
        }
    }
    public class GenerateColumnIdData
    {
        public string? FileName { get; set; }
        public string Name { get; set; }
        public string Field { get; set; }
        public bool OverrideExisting { get; set; }
        public int? StartFrom { get; set; }

        public GenerateColumnIdData()
        {
            Name = string.Empty;
            Field = string.Empty;
            OverrideExisting = false;
        }
    }
    public class ColumnData
    {
        public string ColumnName { get; set; }
        public object? Value { get; set; }
        public string? ReferenceId { get; set; }
        public object? FallBackValue { get; set; }

        public ColumnData()
        {
            ColumnName = string.Empty;
            Value = 0;
            ReferenceId = null;
        }
    }
}
