using DBCD;
using DBCD.IO;
using DBCD.Providers;
using System.Text.Json;

namespace DBXPatching.Core
{
    public enum PatchingResultCode
    { 
        OK = 0,
        ERROR_INVALID_ARGUMENT =2,
        ERROR_INSERTING_RECORD_IN_DB = 3,
        ERROR_REFERENCE_NOT_FOUND = 4,
        ERROR_INVALID_REFERENCE_NAME = 5,
        ERROR_INVALID_REFERENCE_FIELD = 6,
        ERROR_SETTING_VALUE = 7,
        ERROR_INVALID_LOOKUP_INSRUCTION = 8,
        ERROR_LOOKUP_FAILED = 9,
        ERROR_INVALID_FIELD_REFERENCE = 10,
        ERROR_INVALID_VALUE_FOR_FIELD = 11,
        ERROR_UPDATE_RECORD_ID_NOT_FOUND = 12,
        ERROR_DB2_FILE_DOES_NOT_EXIST = 13,
    }

    public class DBXPatchingOperationResult
    {
        public PatchingResultCode ResultCode { get; set; } = PatchingResultCode.OK;
        public string[] Messages { get; set;} = [];

        public static DBXPatchingOperationResult Ok {
            get { return new DBXPatchingOperationResult { ResultCode = PatchingResultCode.OK }; }
        }
    }

    public class DBXPatcher
    {
        public string DBCInputDirectory { get; set; }
        public string DBCOutputDirectory { get; set; }

        private readonly IDBDProvider _dbdProvider;
        private readonly IDBCProvider _dbcProvider;
        private readonly DBCD.DBCD _dbcd;

        private readonly Dictionary<string, IDBCDStorage> openedFiles;

        private readonly Dictionary<string, int> _referenceIds;

        private readonly List<string> _modifiedFiles;

        public DBXPatcher(string inputDir, string outputDir)
        {
            DBCInputDirectory = inputDir;
            DBCOutputDirectory = outputDir;

            openedFiles = [];
            _referenceIds = [];
            _modifiedFiles = [];

            _dbdProvider = new GithubDBDProvider();
            _dbcProvider = new FilesystemDBCProvider(inputDir);
            _dbcd = new DBCD.DBCD(_dbcProvider, _dbdProvider);
        }

        public DBXPatchingOperationResult ApplyPatch(Patch patch)
        {
            foreach (var instruction in patch.Lookup)
            {
                var result = ApplyLookupRecordInstructions(instruction);
                if (result.ResultCode != PatchingResultCode.OK)
                {
                    return result;
                }
            }
            foreach (var instruction in patch.Add)
            {
                var result = ApplyAddRecordInstructions(instruction);
                if (result.ResultCode != PatchingResultCode.OK)
                {
                    return result;
                }
            }
            foreach (var instruction in patch.Update)
            {
                var result = ApplyUpdateRecordInstruction(instruction);
                if (result.ResultCode != PatchingResultCode.OK)
                {
                    return result;
                }
            }

            foreach (var file in _modifiedFiles)
            {
                openedFiles[file].Save(Path.Join(DBCOutputDirectory, file));
            }

            return DBXPatchingOperationResult.Ok;
        }

        private DBXPatchingOperationResult ApplyLookupRecordInstructions(LookupRecordInstruction instruction)
        {
            var result = OpenDb(instruction.Filename, out var records);
            if (result.ResultCode != PatchingResultCode.OK) { return result; }
            if (string.IsNullOrEmpty(instruction.Field) || instruction.SearchValue == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INVALID_LOOKUP_INSRUCTION,
                    Messages = [$"Found lookup instruction without with invalid field or searchvalue for file '{instruction.Filename}'."]
                };
            } 
            if (instruction.SearchValue is JsonElement element)
            {
                var convertResult = ConvertJsonToFieldType(element, instruction.Filename, instruction.Field, out var convertedVal);
                if (convertResult.ResultCode != PatchingResultCode.OK)
                {
                    return convertResult;
                }
                instruction.SearchValue = convertedVal!;
            }
            foreach (var row in records!.Values)
            {
                if (row[instruction.Field].Equals(instruction.SearchValue))
                {
                    ProcessSaveReferences(row, instruction.SaveReferences);
                    return DBXPatchingOperationResult.Ok;
                }
            }
            if (instruction.IgnoreFailure)
            {
                return DBXPatchingOperationResult.Ok;
            }

            return new DBXPatchingOperationResult() {
                ResultCode = PatchingResultCode.ERROR_LOOKUP_FAILED
            };
        }

        private DBXPatchingOperationResult ApplyAddRecordInstructions(AddRecordInstruction instruction)
        {
            var result = OpenDb(instruction.Filename, out var records);
            if (result.ResultCode != PatchingResultCode.OK) { return result; }


            DBCDRow? row;
            if (instruction.RecordId != null && records!.Keys.Any(x => x == instruction.RecordId))
            {
                row = records[instruction.RecordId.Value];
            } else
            {
                var id = instruction.RecordId ?? (records!.Count > 0 ? records!.Keys.Max() : 1);
                row = records!.ConstructRow(id);
                row[row.GetDynamicMemberNames().First()] = id;
                row.ID = id;
                records!.Add(id, row);
            }

            if (row == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INSERTING_RECORD_IN_DB,
                    Messages = [$"Unable to add a record to file '{instruction.Filename}'"]
                };
            }

            foreach(var generateId in instruction.GenerateIds)
            {
                if (string.IsNullOrEmpty(generateId.Name))
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_INVALID_REFERENCE_NAME,
                        Messages = [$"Found generate id instruction without reference name for file '{instruction.Filename}'."]
                    };
                }
                if (string.IsNullOrEmpty(generateId.Field))
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_INVALID_REFERENCE_FIELD,
                        Messages = [$"Found generate id instruction without reference field for file '{instruction.Filename}'."]
                    };
                }
                if (_referenceIds.ContainsKey(generateId.Name) && !generateId.OverrideExisting)
                {
                    continue;
                }
                var searchRecords = records;
                if (!string.IsNullOrEmpty(generateId.FileName))
                {
                    result = OpenDb(generateId.FileName, out searchRecords);
                    if (result.ResultCode != PatchingResultCode.OK) { return result; }
                }
                _referenceIds[generateId.Name] = generateId.StartFrom ?? 1;
                foreach(var searchRow in searchRecords!.Values)
                {
                    var compareVal = searchRow.FieldAs<int>(generateId.Field);
                    if (compareVal >= _referenceIds[generateId.Name])
                    {
                        _referenceIds[generateId.Name] = compareVal + 1;
                    }
                }
            }

            if (!string.IsNullOrEmpty(instruction.RecordIdReference))
            {
                records.Remove(row.ID);
                row.ID = _referenceIds[instruction.RecordIdReference];
                var idFieldName = row.GetDynamicMemberNames().First();
                DBCDRowHelper.SetDBCRowColumn(row, idFieldName, _referenceIds[instruction.RecordIdReference]);
                records.Add(row.ID, row);
            }


            var updateResult = SetColumnDataForRecord(row, instruction.Filename, instruction.Record);
            if (updateResult.ResultCode != PatchingResultCode.OK) {
                return updateResult;
            }
            
            ProcessSaveReferences(row, instruction.SaveReferences);
            if (!_modifiedFiles.Contains(instruction.Filename))
            {
                _modifiedFiles.Add(instruction.Filename);
            }
            return DBXPatchingOperationResult.Ok;
        }
       
        private DBXPatchingOperationResult ApplyUpdateRecordInstruction(UpdateRecordInstruction instruction)
        {
            var result = OpenDb(instruction.Filename, out var records);
            if (result.ResultCode != PatchingResultCode.OK) { return result; }
            DBCDRow? record = null;

            if (!string.IsNullOrEmpty(instruction.Field))
            {
                foreach (var row in records!.Values)
                {
                    if (row[instruction.Field].Equals(instruction.RecordId))
                    {
                        record = row;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(instruction.Field) && records!.ContainsKey(instruction.RecordId))
            {
                record = records![instruction.RecordId];
            }
            if (record == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_UPDATE_RECORD_ID_NOT_FOUND,
                    Messages = [$"Unable to find record with id: '{instruction.RecordId}{(string.IsNullOrEmpty(instruction.Field) ? "" : $" for column: {instruction.Field}")}' in file: '{instruction.Filename}'."]
                };
            }

            if (!_modifiedFiles.Contains(instruction.Filename))
            {
                _modifiedFiles.Add(instruction.Filename);
            }
            return SetColumnDataForRecord(record, instruction.Filename, instruction.Record);
        }

        private DBXPatchingOperationResult SetColumnDataForRecord(DBCDRow row, string fileName, List<ColumnData> columns)
        {
            foreach (var col in columns)
            {
                try
                {
                    if (!string.IsNullOrEmpty(col.ReferenceId) && _referenceIds.ContainsKey(col.ReferenceId))
                    {
                        DBCDRowHelper.SetDBCRowColumn(row, col.ColumnName, _referenceIds[col.ReferenceId]);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(col.ReferenceId) && col.FallBackValue is JsonElement element)
                    {
                        var convertResult = ConvertJsonToFieldType(element, fileName, col.ColumnName, out var convertedVal);
                        if (convertResult.ResultCode != PatchingResultCode.OK)
                        {
                            return convertResult;
                        }
                        col.FallBackValue = convertedVal!;
                        DBCDRowHelper.SetDBCRowColumn(row, col.ColumnName, col.FallBackValue);
                        continue;
                    }
                    if (!string.IsNullOrEmpty(col.ReferenceId))
                    {
                        return new DBXPatchingOperationResult()
                        {
                            ResultCode = PatchingResultCode.ERROR_REFERENCE_NOT_FOUND,
                            Messages = [$"Unable to find referenced instruction with name '{col.ReferenceId}'"]
                        };
                    }
                    if (col.Value is JsonElement valueElement)
                    {
                        var convertResult = ConvertJsonToFieldType(valueElement, fileName, col.ColumnName, out var convertedVal);
                        if (convertResult.ResultCode != PatchingResultCode.OK)
                        {
                            return convertResult;
                        }
                        col.Value = convertedVal!;
                        DBCDRowHelper.SetDBCRowColumn(row, col.ColumnName, col.Value!);
                    }
                }
                catch
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_SETTING_VALUE,
                        Messages = [$"Unable to set field '{col.ColumnName}' to '{col.Value}'"]
                    };
                }
            }
            return DBXPatchingOperationResult.Ok;
        }

        private DBXPatchingOperationResult OpenDb(string fileName, out IDBCDStorage? storage)
        {
            var db2Name = Path.GetFileName(fileName);
            if (openedFiles.ContainsKey(db2Name))
            {
                storage = openedFiles[db2Name];
                return DBXPatchingOperationResult.Ok;
            }

            var db2Path = Path.Combine(DBCInputDirectory, db2Name);
            if (!File.Exists(db2Path)) {
                storage = null;
                return new DBXPatchingOperationResult()
                {
                    Messages = [$"File '{db2Path}' does not exist."],
                    ResultCode = PatchingResultCode.ERROR_DB2_FILE_DOES_NOT_EXIST
                };
            }

            storage = _dbcd.Load(db2Path, "9.2.7.45745", Locale.EnUS);
            openedFiles[db2Name] = storage;
            return DBXPatchingOperationResult.Ok;
        }

        private void ProcessSaveReferences(DBCDRow row, List<ReferenceColumnData> instructions)
        {
            foreach (var reference in instructions)
            {
                if (string.IsNullOrEmpty(reference.Name))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(reference.Field))
                {
                    _referenceIds[reference.Name] = row.ID;
                }
                else
                {
                    _referenceIds[reference.Name] = Convert.ToInt32(row[reference.Field]);
                }
            }
        }

        public DBXPatchingOperationResult ConvertJsonToFieldType(JsonElement element, string fileName, string field, out object? resultValue)
        {
            try
            {
                var resultType = DBCDRowHelper.GetFieldType(openedFiles[fileName].ConstructRow(0), field);
                resultValue = element.Deserialize(resultType);
                if (resultValue == null)
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_INVALID_VALUE_FOR_FIELD,
                        Messages = [$"Found instruction without with invalid value '{element}' for file '{fileName}'."]
                    };
                }
                return DBXPatchingOperationResult.Ok;
            } catch
            {
                resultValue = null;
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INVALID_FIELD_REFERENCE,
                    Messages = [$"Found instruction with invalid field reference '{field}' for file '{fileName}'."]
                };
            }
        }
    }
}
