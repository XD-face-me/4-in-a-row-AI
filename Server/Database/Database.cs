﻿using System.IO;
using Engine;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Diagnostics;

namespace Server
{
    /// <summary>
    /// A database object used to read and write the database format.
    /// </summary>
    public class Database : IDisposable
    {
        public DatabaseProperties DbProperties;
        private List<FileStream>[] FieldStream;
        private List<FileStream>[] FieldDataStream;
        private Dictionary<Field, int>[] Fields;

        private bool Busy;

        /// <summary>
        /// Creates a new database instance from the given path.
        /// </summary>
        /// <param name="path">Database storage path</param>
        public Database(string path)
        {
            if (!Directory.Exists(path))
                throw new DatabaseException($"Database doesn't exist. The given database directory doesn´t exist ({path})");
            
            string[] files = Directory.GetFiles(path);

            if (files.Length > 0)
            {
                string propertiesPath = files[0];
                if (!propertiesPath.EndsWith("Properties") || !File.Exists(propertiesPath))
                    throw new DatabaseException($"Database incomplete. Properties file not found in: {path}");
            }
            else
                throw new DatabaseException($"Database incomplete. Properties file not found in: {path}");
            
            DbProperties = new DatabaseProperties(path);
            loadStreams();
        }

        /// <summary>
        /// Creates a new database at the given path.
        /// </summary>
        /// <param name="dbProperties"></param>
        public static Database createNew(DatabaseProperties dbProperties)
        {
            prepareNew(dbProperties);
            Database db = new Database(dbProperties.Path);
            return db;
        }

        /// <summary>
        /// Creates a new database at the given path and returns an instance of this new database.
        /// </summary>
        /// <param name="dbProperties"></param>
        public static void prepareNew(DatabaseProperties dbProperties)
        {
            if (Directory.Exists(dbProperties.Path))
                throw new DatabaseException("Database path already exists. Make sure you use a database path that doesn't exist already.");

            Directory.CreateDirectory(dbProperties.Path);

            byte maxSize = dbProperties.MaxFieldStorageSize;
            string dbPath = dbProperties.Path;

            for (byte i = 1; i <= maxSize; i++)
            {
                Directory.CreateDirectory(dbProperties.getFieldDirPath(i));
                File.Create(DatabaseLocation.getFieldPath(dbProperties, i, 0)).Dispose();
                File.Create(DatabaseLocation.getFieldDataPath(dbProperties, i, 0)).Dispose();
            }

            dbProperties.writeProperties();
        }

        /// <summary>
        /// Opens all database files to access them later on. Als loads all fields from the database into memory.
        /// </summary>
        private void loadStreams()
        {
            int maxStorageSize = DbProperties.MaxFieldStorageSize;
            FieldStream = new List<FileStream>[maxStorageSize];
            FieldDataStream = new List<FileStream>[maxStorageSize];
            Fields = new Dictionary<Field, int>[maxStorageSize];

            for (byte i = 1; i <= DbProperties.MaxFieldStorageSize; i++)
            {
                string dbPath = DbProperties.Path;
                if (!Directory.Exists(dbPath))
                    throw new DatabaseException($"Database incomplete. Directory {DbProperties.getFieldDirPath(i)} doesn't exist.");

                int fileCount = Math.Max(1, DbProperties.getFieldFileCount(i));
                FieldStream[i - 1] = new List<FileStream>();
                FieldDataStream[i - 1] = new List<FileStream>();
                ConcurrentDictionary<Field, int> fields = new ConcurrentDictionary<Field, int>();

                for (int j = 0; j < fileCount; j++)
                {
                    string fPath = DatabaseLocation.getFieldPath(DbProperties, i, j);
                    FileStream fStream = new FileStream(fPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                    string fdPath = DatabaseLocation.getFieldDataPath(DbProperties, i, j);
                    FileStream fdStream = new FileStream(fdPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                    byte[] bytes = new byte[fStream.Length];
                    fStream.Read(bytes, 0, bytes.Length);

                    Parallel.For(0, bytes.Length / i, k =>
                    {
                        byte[] fStorage = new byte[i];
                        Buffer.BlockCopy(bytes, k * i, fStorage, 0, i);
                        Field f = fStorage.decompressField();
                        DatabaseLocation dbLoc = new DatabaseLocation(DbProperties, i, j, k);
                        fields.GetOrAdd(f, dbLoc.GlobalLocation);
                    });

                    FieldStream[i - 1].Add(fStream);
                    FieldDataStream[i - 1].Add(fdStream);
                }

                Fields[i - 1] = new Dictionary<Field, int>();

                foreach (KeyValuePair<Field, int> pair in fields)
                {
                    Fields[i - 1].Add(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>
        /// Returns whether the given field is included in the database.
        /// </summary>
        /// <param name="field">The field to check the existance for</param>
        /// <returns>Existance of field</returns>
        public bool fieldExists(Field field)
        {
            int i = field.compressField().Length;
            return Fields[i - 1].ContainsKey(field);
        }

        /// <summary>
        /// Returns whether the given field is included in the database.
        /// </summary>
        /// <param name="field">The field to check the existance of</param>
        /// <param name="dbLocation">The location of the field within the database</param>
        /// <returns>Existance of field</returns>
        public bool fieldExists(Field field, out DatabaseLocation dbLocation)
        {
            int i = field.compressField().Length;
            bool exists = Fields[i - 1].ContainsKey(field);
            if (exists)
                dbLocation = new DatabaseLocation(DbProperties, i, Fields[i - 1][field]);
            else
                dbLocation = DatabaseLocation.NonExisting;

            return exists;
        }

        /// <summary>
        /// Returns the content of a specified range (of fields) in the database for the given fieldLength.
        /// </summary>
        /// <param name="fieldLength"></param>
        /// <param name="beginRange">Startpoint of the range</param>
        /// <param name="endRange">Endpoint of the range</param>
        /// <returns></returns>
        public byte[] getFieldFileContent(int fieldLength, int beginRange, int endRange)
        {
            byte[] result = new byte[(endRange - beginRange) * fieldLength];
            int arrayPos = 0;

            DatabaseLocation beginLoc = new DatabaseLocation(DbProperties, fieldLength, beginRange);
            DatabaseLocation endLoc = new DatabaseLocation(DbProperties, fieldLength, endRange);

            int globalLoc = beginRange;
            int maxFieldsInFile = DbProperties.getMaxFieldsInFile(fieldLength);

            while (globalLoc < endRange)
            {
                DatabaseLocation dbLoc = new DatabaseLocation(DbProperties, fieldLength, globalLoc);

                int start = dbLoc.getFieldsSeekPosition();
                int end = 0;

                int fileIndex = dbLoc.FileIndex;
                int endFileIndex = endLoc.FileIndex;

                if (fileIndex < endFileIndex)
                    end = maxFieldsInFile * fieldLength;
                else
                    end = endLoc.getFieldsSeekPosition();

                int count = end - start;

                FieldStream[fieldLength - 1][fileIndex].Seek(start, SeekOrigin.Begin);
                FieldStream[fieldLength - 1][fileIndex].Read(result, arrayPos, count);

                arrayPos += count;
                globalLoc += count / fieldLength;
            }

            return result;
        }

        /// <summary>
        /// Reads the field data from of the given field the database.
        /// </summary>
        /// <param name="field">Field to read the data from</param>
        /// <returns></returns>
        public FieldData readFieldData(Field field)
        {
            DatabaseLocation location;
            if (fieldExists(field, out location))
                return readFieldData(location);
            else
                throw new DatabaseException($"Can't read field data, because the given field doesn't exist.");
        }

        /// <summary>
        /// Reads the field data from the database at the specified database location.
        /// </summary>
        /// <param name="dbLocation">Field location</param>
        /// <returns>The requested field data</returns>
        public FieldData readFieldData(DatabaseLocation dbLocation)
        {
            FileStream fieldDataStream = FieldDataStream[dbLocation.FieldLength - 1][dbLocation.FileIndex];
            int seekPosition = dbLocation.getFieldDataSeekPosition();

            uint[] storage = new uint[DbProperties.FieldWidth * 2];
            BinaryReader br = new BinaryReader(fieldDataStream);
            fieldDataStream.Seek(seekPosition, SeekOrigin.Begin);   // Sets the reading position to the wanted byte (uint in our case) database.

            for (byte i = 0; i < DbProperties.FieldWidth * 2; i++)  // We read the uints one by one from the database.
            {
                storage[i] = br.ReadUInt32();
            }

            return new FieldData(storage);
        }

        /// <summary>
        /// Writes the given field data to the database for the specified field.
        /// </summary>
        /// <param name="field">The field to save the data for</param>
        /// <param name="fieldData">The field data to write</param>
        public void writeFieldData(Field field, FieldData fieldData)
        {
            DatabaseLocation location;
            if (fieldExists(field, out location))
                writeFieldData(location, fieldData);
            else
                throw new DatabaseException($"Can't write field data, because the given field doesn't exist.");
        }

        /// <summary>
        /// Writes the given field data to the database at the specified database location.
        /// </summary>
        /// <param name="dbLocation">Field location</param>
        /// <param name="fieldData">Data to be written</param>
        public void writeFieldData(DatabaseLocation dbLocation, FieldData fieldData)
        {
            //if (!dbLocation.locationExists())
            //    throw new DatabaseException($"Can't write field data at field location. {dbLocation}. This location doesn't exist.");
            
            FileStream fieldDataStream = FieldDataStream[dbLocation.FieldLength - 1][dbLocation.FileIndex];
            int seekPosition = dbLocation.getFieldDataSeekPosition();

            BinaryWriter bw = new BinaryWriter(fieldDataStream);
            uint[] storage = fieldData.getStorage();

            fieldDataStream.Seek(seekPosition, SeekOrigin.Begin);   // Sets the writing position to the wanted byte (uint in our case) in the database.
            for (byte i = 0; i < DbProperties.FieldWidth * 2; i++)  // We write each uint of the storage to the database stream.
            {
                bw.Write(storage[i]);
            }
        }

        /// <summary>
        /// Allocates a new database location for the specified field by increasing the length of the database.
        /// </summary>
        /// <param name="f">The field that's going to be added</param>
        /// <returns>The database location for the new field</returns>
        private DatabaseLocation allocateNextDatabaseLocation(Field f)
        {
            return allocateNextDatabaseLocation(f.compressField().Length);
        }

        /// <summary>
        /// Allocates a new database location for the specified field length by increasing the length of the database.
        /// </summary>
        /// <param name="fieldLength">The length of the field that's going to be added</param>
        /// <returns>The database location for the new field</returns>
        private DatabaseLocation allocateNextDatabaseLocation(int fieldLength)
        {
            DbProperties.increaseLength(fieldLength, false);
            int fileIndex = DbProperties.getFieldFileCount(fieldLength) - 1;
            return new DatabaseLocation(DbProperties, fieldLength, fileIndex, DbProperties.getLength(fieldLength) % DbProperties.getMaxFieldsInFile(fieldLength) - 1);
        }

        /// <summary>
        /// Writes the given field data to an existing location in the database (location in bytes).
        /// </summary>
        /// <param name="seekPosition">Location in field data file in bytes</param>
        /// <param name="fieldData">Data that needs to be merged with the existing field data</param>
        /// <param name="fieldDataStream"></param>
        public void processFieldData(int seekPosition, FieldData fieldData, FileStream fieldDataStream)
        {
            fieldDataStream.Seek(seekPosition, SeekOrigin.Begin);

            BinaryWriter bw = new BinaryWriter(fieldDataStream);
            BinaryReader br = new BinaryReader(fieldDataStream);

            uint[] storage = fieldData.getStorage();

            if (seekPosition < fieldDataStream.Length)
            {
                for (byte i = 0; i < DbProperties.FieldWidth * 2; i++)
                {
                    storage[i] += br.ReadUInt32();
                }
            }

            fieldDataStream.Seek(seekPosition, SeekOrigin.Begin);   // Sets the writing position to the wanted byte (uint in our case) in the database.

            for (byte i = 0; i < DbProperties.FieldWidth * 2; i++)  // We write each uint of the storage to the database stream.
            {
                bw.Write(storage[i]);
            }
        }

        /// <summary>
        /// Adds the given field to the database. WARNING: It's your own responsibility to check for the existance of a field in the database. Always AVOID adding fields that are already included in the database.
        /// </summary>
        /// <param name="field">Field to be added</param>
        /// <returns>The DatabaseLocation of the new field</returns>
        public DatabaseLocation addDatabaseItem(Field field)
        {
            byte[] compressed = field.compressField();                  // Gets the compressed field.
            int length = compressed.Length;
            DatabaseLocation dbLoc = allocateNextDatabaseLocation(length);

            Fields[length - 1].Add(field, dbLoc.GlobalLocation);

            FileStream fieldStream = FieldStream[length - 1][dbLoc.FileIndex];
            fieldStream.Seek(0, SeekOrigin.End);                    // Sets the writing position to the end of the database.
            fieldStream.Write(compressed, 0, length);    // Writes the bytes of the compressed field to the database.


            FileStream fieldDataStream = FieldDataStream[dbLoc.FieldLength - 1][dbLoc.FileIndex];
            fieldDataStream.Seek(0, SeekOrigin.End);
            fieldDataStream.Write(new byte[DbProperties.FieldWidth * 8], 0, DbProperties.FieldWidth * 8);

            return dbLoc;
        }

        public void processGameHistory(Dictionary<Field, FieldData> data)
        {
            // Initialization and sorting of data Dictionary.
            byte maxFS = DbProperties.calculateMaxFieldStorageSize();
            Dictionary<Field, FieldData>[] cData = new Dictionary<Field, FieldData>[maxFS];
            for (byte i = 0; i < maxFS; i++)
            {
                cData[i] = new Dictionary<Field, FieldData>();
            }

            foreach (KeyValuePair<Field, FieldData> pair in data)
            {
                int length = pair.Key.compressField().Length;
                cData[length - 1].Add(pair.Key, pair.Value);
            }

            for (int i = 0; i < maxFS; i++)
            {
                // Processes the fielddata of all fields that are already included in the database.
                List<Field> matches;
                updateExistingFieldData(cData[i], i + 1, out matches);

                // Determination of the fields that need to be added to the database.
                Dictionary<int, List<Field>> newFields = getNewFieldsDictionary(i + 1, cData[i], matches);

                // Addition of new fields.
                if (newFields.Any())
                    addFields(i + 1, cData[i], newFields);
            }

            DbProperties.writeProperties();
        }

        private void addFields(int i, Dictionary<Field, FieldData> dictionary, Dictionary<int, List<Field>> newFields)
        {
            int newestFile = newFields.Max(p => p.Key);

            int streamCount = FieldStream[i - 1].Count;

            while (streamCount <= newestFile)
            {
                string newFieldPath = DatabaseLocation.getFieldPath(DbProperties, i, streamCount);
                FileStream fStream = new FileStream(newFieldPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                FieldStream[i - 1].Add(fStream);

                string newDataPath = DatabaseLocation.getFieldDataPath(DbProperties, i, streamCount);
                FileStream fdStream = new FileStream(newDataPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                FieldDataStream[i - 1].Add(fdStream);
                streamCount++;
            }

            foreach (KeyValuePair<int, List<Field>> pair in newFields)
            {
                List<byte> bytes = new List<byte>();

                int fileIndex = pair.Key;

                FileStream fieldDataStream = FieldDataStream[i - 1][fileIndex];
                BinaryWriter bw = new BinaryWriter(fieldDataStream);
                foreach (Field f in newFields[fileIndex])
                {
                    bytes.AddRange(f.compressField());

                    fieldDataStream.Seek(0, SeekOrigin.End);

                    uint[] fdStorage = dictionary[f].getStorage();

                    for (int k = 0; k < fdStorage.Length; k++)
                    {
                        bw.Write(fdStorage[k]);
                    }
                }

                FileStream fieldStream = FieldStream[i - 1][fileIndex];
                fieldStream.Seek(0, SeekOrigin.End);
                fieldStream.Write(bytes.ToArray(), 0, bytes.Count);

                fieldStream.Flush();
                fieldDataStream.Flush();
            }
        }

        private Dictionary<int, List<Field>> getNewFieldsDictionary(int i, Dictionary<Field, FieldData> dictionary, List<Field> matches)
        {
            Dictionary<int, List<Field>> sorted = new Dictionary<int, List<Field>>();
            List<Field> newFields = new List<Field>();
            
            //List<Field> relevant = dictionary.Keys.AsParallel().Where(f => !matches.Contains(f)).ToList();
            int prevFileIndex = -1;

            foreach (Field f in dictionary.Keys.AsParallel().Where(f => !matches.Contains(f)))
            {
                DatabaseLocation dbLoc = allocateNextDatabaseLocation(i);
                Fields[i - 1].Add(f, dbLoc.GlobalLocation);
                
                int fileIndex = dbLoc.FileIndex;

                if (fileIndex != prevFileIndex)
                {
                    sorted.Add(prevFileIndex, newFields.ToList());
                    prevFileIndex = fileIndex;
                    newFields = new List<Field>();
                }

                newFields.Add(f);
            }

            sorted.Add(prevFileIndex, newFields);

            sorted.Remove(-1);

            return sorted;
        }

        private void updateExistingFieldData(Dictionary<Field, FieldData> dictionary, int i, out List<Field> matches)
        {
            ConcurrentBag<Field> fList = new ConcurrentBag<Field>();

            int fileCount = DbProperties.getFieldFileCount(i);

            for (int j = 0; j < fileCount; j++)
            {
                FileStream fieldDataStream = FieldDataStream[i - 1][j];

                ConcurrentDictionary<int, FieldData> locations = new ConcurrentDictionary<int, FieldData>();
                
                Parallel.ForEach(Fields[i - 1].AsParallel().Where(f => dictionary.ContainsKey(f.Key)), p =>
                {
                    DatabaseLocation dbLoc = new DatabaseLocation(DbProperties, i, p.Value);
                    locations.GetOrAdd(dbLoc.getFieldDataSeekPosition(), dictionary[p.Key]);
                    fList.Add(p.Key);
                });

                foreach (KeyValuePair<int, FieldData> l in locations)
                {
                    processFieldData(l.Key, l.Value, fieldDataStream);
                }
            }

            matches = fList.ToList();
        }

        public void setBusy(bool busy)
        {
            Busy = busy;
        }

        public bool isBusy()
        {
            return Busy;
        }
        /*
        /// <summary>
        /// Clears the database item (the field and its corresponding data) of the specified field from the database(s). This function doesn't delete the item, but sets all its values to zero.
        /// </summary>
        /// <param name="field">The field to be cleared</param>
        public void clearDataBaseItem(Field field)
        {
            DatabaseLocation dbLocation;
            if (!fieldExists(field, out dbLocation))
                throw new DatabaseException($"Can't clear database item at database location. {dbLocation}. This location doesn't exist.");

            FileStream fieldStream = FieldStream[dbLocation.FieldLength - 1][dbLocation.FileIndex];
            fieldStream.Seek(dbLocation.getFieldsSeekPosition(), SeekOrigin.Begin);
            fieldStream.Write(new byte[dbLocation.FieldLength], 0, dbLocation.FieldLength);

            FileStream fieldDataStream = FieldDataStream[dbLocation.FieldLength - 1][dbLocation.FileIndex];
            fieldDataStream.Seek(dbLocation.getFieldDataSeekPosition(), SeekOrigin.Begin);
            fieldDataStream.Write(new byte[DbProperties.FieldWidth * 8], 0, DbProperties.FieldWidth * 8);
        }
        */
        public void Dispose()
        {
            for (int i = 0; i < FieldStream.Length; i++)
            {
                foreach (FileStream fs in FieldStream[i])
                {
                    fs.Dispose();
                }

                foreach (FileStream fs in FieldDataStream[i])
                {
                    fs.Dispose();
                }
            }
        }
    }
}
