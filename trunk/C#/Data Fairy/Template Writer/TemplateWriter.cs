﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.IO;
using net.mkv25.DataFairy.VO;

namespace net.mkv25.writer
{
    public class TemplateWriter
    {
        /** The dataset to base the code generation on */
        public DataFairyFile sourceDataSet;

        /** name of this template */
        public string name;

        /** author of the template */
        public string author;

        /** contact info for this template */
        public string contact;

        /** The template for the database file */
        public TemplateFile dataBaseFileTemplate;

        /** The template for individual table class files */
        public TemplateFile tableFileTemplate;

        /** The template for individual row class files */
        public TemplateFile rowFileTemplate;

        /** The template for enumeration lists */
        public TemplateFile enumerationFileTemplate;

        /** Any other template files that exist in the package */
        public List<TemplateFile> packageFileTemplates;

        /** A list of strings to search and replace on */
        public Dictionary<string, string> basicTypes;

        /** A list of strings to search and replace on */
        public List<KeyValuePair<string, string>> templateVariables;

        /* A list of messages generated during the write process */
        public List<KeyValuePair<DateTime, string>> logMessages;

        /** The seperator to use between generated package paths */
        public string packageSeperator = ".";

        /** Should the code template generate folders that match the package structure */
        public bool generatePackageFolderStructure = false;

        /** Should the package name be forced to lowercase for a given template */
        public bool forceLowercasePackageStructure = false;

        /** A fragment for writing new class properties */
        public TemplateFragment classPropertyFragment;

        /** A fragment for writing new class level variables */
        public TemplateFragment classVariableFragment;

        /** A fragment for defining a constant */
        public TemplateFragment constantFragment;

        /** A fragment for variable assignment */
        public TemplateFragment localVariableFragment;

        /** A fragment for variable assignment */
        public TemplateFragment localAssignmentFragment;

        /** A fragment for writing new class instances */
        public TemplateFragment newClassInstanceFragment;

        /** A fragment for writing parameters */
        public TemplateFragment parameterFragment;
        
        /** The package path to use for code generation */
        protected string _packageString;

        public TemplateWriter()
        {
            templateVariables = new List<KeyValuePair<string, string>>();
        }

        /** The package path for code generation, including the modifier for lowercase mode */
        public string PackageString 
        {
            get
            {
                if (forceLowercasePackageStructure)
                {
                    return _packageString.ToLower();
                }
                return _packageString;
            }
            set
            {
                _packageString = value;
            }
        }

        /** Add a template variable to be searched and replaced */
        public void AddTemplateVariable(string key, string value)
        {
            templateVariables.Add(new KeyValuePair<string, string>(key, value));
        }

        /** Lookup function to convert local type in to language specific type */
        protected string getBasicType(string requestedType)
        {
            // convert "lookup" into int field
            if (requestedType == "lookup")
                return getBasicType("int");

            if (basicTypes.ContainsKey(requestedType))
                return basicTypes[requestedType];
            return requestedType;
        }

        /**
         * Generate code from templates into the specified directory.
         */
        public void WriteTo(string outputDirectory)
        {
            logMessages = new List<KeyValuePair<DateTime, string>>();

            if (String.IsNullOrEmpty(outputDirectory))
            {
                Log("No output directory set");
                return;
            }

            if (!Directory.Exists(outputDirectory))
            {
                Log("Output path does not exist.");
                return;
            }

            Log("Starting Write Process");

            // delete down files that already exist
            var directoryBrowser = new DirectoryInfo(outputDirectory);
            directoryBrowser.GetFiles("*", SearchOption.AllDirectories).ToList().ForEach(file => file.Delete());

            // check to see if a folder structure needs generating for the packages
            if (generatePackageFolderStructure)
            {
                var packagePath = new StringBuilder();
                var packagePathArray = PackageString.Split(packageSeperator.ToCharArray());
                foreach (var packagePart in packagePathArray)
                {
                    packagePath.Append(packagePart + "\\");
                }
                outputDirectory = outputDirectory + "\\" + packagePath.ToString();
            }

            // do the work
            WritePackageFiles(outputDirectory);
            WriteEnumerations(outputDirectory);
            WriteRowFiles(outputDirectory);
            WriteTableFiles(outputDirectory);
            WriteDatabaseFile(outputDirectory);

            Log("Finished Write Process");
        }

        /**
         * Generate enumeration files for all rows in all tables in the dataset using a template file.
         */
        public void WriteEnumerations(string outputDirectory)
        {
            if (enumerationFileTemplate == null)
                return;

            string fileName;
            string FOLDER = Path.GetDirectoryName(enumerationFileTemplate.FileName);
            string EXT = Path.GetExtension(enumerationFileTemplate.FileName);
            foreach(DataTable table in sourceDataSet.Tables)
            {
                var className = NameUtils.FormatClassName(table.TableName) + "Enum";
                fileName = className + EXT;

                // build the file
                StringBuilder fileContents = new StringBuilder(enumerationFileTemplate.FileContents);
                StringBuilder variableList = new StringBuilder();

                // build the list of constants for the enum
                variableList.AppendLine("// code generated list of all rows");
                foreach (DataRow row in table.Rows)
                {
                    var variableName = NameUtils.FormatClassConstantName(row["Name"].ToString());
                    variableList.AppendLine(constantFragment.WriteConstant(variableName, getBasicType("int"), row["Id"].ToString()));
                }

                // replace standard set of variables
                ReplaceVariables(fileContents, templateVariables);
                fileContents.Replace("PACKAGE_STRING", PackageString);
                fileContents.Replace("CLASS_NAME", className);
                fileContents.Replace("VARIABLE_LIST", variableList.ToString().TrimEnd('\n', '\r'));

                // write the file
                var filePath = outputDirectory + "\\" + FOLDER + "\\" + fileName;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                StreamWriter writer = new StreamWriter(filePath);
                writer.Write(fileContents);
                writer.Close();

                Log("Created Enum File - " + fileName);
            }
        }

        /**
         * Generate class files for all row types in the dataset using a template file
         */
        public void WriteRowFiles(string outputDirectory)
        {
            if (rowFileTemplate == null)
                return;

            string fileName;
            string FOLDER = Path.GetDirectoryName(rowFileTemplate.FileName);
            string EXT = Path.GetExtension(rowFileTemplate.FileName);
            foreach (DataFairyTable table in sourceDataSet.Tables)
            {
                var className = NameUtils.FormatClassName(table.TableName) + "Row";
                fileName = className + EXT;

                // build the file
                StringBuilder fileContents = new StringBuilder(rowFileTemplate.FileContents);
                StringBuilder variableList = new StringBuilder();
                StringBuilder propertyList = new StringBuilder();
                StringBuilder paramsString = new StringBuilder();
                StringBuilder paramsList = new StringBuilder();

                // start code blocks off with comments
                variableList.AppendLine("// code generated list of variables");
                propertyList.AppendLine("// code generated list of properties");
                paramsList.AppendLine("// code generated list of params");

                // populate list of variables
                foreach (DataFairySchemaField field in table.Schema.AllFields)
                {
                    // create variable for basic types
                    string fieldName = field.FieldName;
                    string fieldType = getBasicType(field.FieldType);

                    // create variable for assignment type
                    var paramName = "_" + NameUtils.FormatVariableName(field.FieldName);
                    var paramType = getBasicType(field.FieldType);
                    if (paramsString.Length > 0)
                        paramsString.Append(", ");

                    // create properties for lookup values
                    if (field.FieldType == "lookup")
                    {
                        paramName = paramName + "Id";
                        propertyList.AppendLine(classPropertyFragment.WriteClassProperty(fieldName, NameUtils.FormatClassName(field.FieldLookUp) + "Row"));
                        variableList.AppendLine(classVariableFragment.WriteClassVariable(fieldName + "Id", fieldType));
                        paramsList.AppendLine(localAssignmentFragment.WriteLocalAssignment(fieldName + "Id", paramName));
                    }
                    else if (field.FieldName == "id")
                    {
                        // id is a required property on the template
                        paramsList.AppendLine(localAssignmentFragment.WriteLocalAssignment(fieldName, paramName));
                    }
                    else
                    {
                        variableList.AppendLine(classVariableFragment.WriteClassVariable(fieldName, fieldType));
                        paramsList.AppendLine(localAssignmentFragment.WriteLocalAssignment(fieldName, paramName));
                    }

                    paramsString.Append(parameterFragment.WriteParameter(paramName, paramType));
                }

                // replace standard set of variables
                ReplaceVariables(fileContents, templateVariables);
                fileContents.Replace("PACKAGE_STRING", PackageString);
                fileContents.Replace("CLASS_NAME", className);
                fileContents.Replace("VARIABLE_LIST", variableList.ToString().TrimEnd('\n', '\r'));
                fileContents.Replace("PROPERTY_LIST", propertyList.ToString().TrimEnd('\n', '\r'));
                fileContents.Replace("CLASS_PARAMS_STRING", paramsString.ToString().TrimEnd('\n', '\r'));
                fileContents.Replace("CLASS_PARAMS_LIST", paramsList.ToString().TrimEnd('\n', '\r'));

                // write the file
                var filePath = outputDirectory + "\\" + FOLDER + "\\" + fileName;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                StreamWriter writer = new StreamWriter(filePath);
                writer.Write(fileContents);
                writer.Close();

                Log("Created Row File - " + fileName);
            }
        }

        /**
         * Generate class files for all rows in all tables in the dataset using a template file
         */
        public void WriteTableFiles(string outputDirectory)
        {
            if (tableFileTemplate == null)
                return;

            string fileName;
            string FOLDER = Path.GetDirectoryName(tableFileTemplate.FileName);
            string EXT = Path.GetExtension(tableFileTemplate.FileName);
            foreach (DataFairyTable table in sourceDataSet.Tables)
            {
                var className = NameUtils.FormatClassName(table.TableName) + "Table";
                var rowClassName = NameUtils.FormatClassName(table.TableName) + "Row";
                fileName = className + EXT;

                // build the file
                StringBuilder fileContents = new StringBuilder(tableFileTemplate.FileContents);
                StringBuilder rowList = new StringBuilder();

                // populate individual data rows
                rowList.AppendLine("// code generated list of all rows");
                foreach (DataRow row in table.Rows)
                {
                    var rowParameters = new StringBuilder();

                    // populate list of variables
                    foreach (DataFairySchemaField field in table.Schema.AllFields)
                    {
                        if (rowParameters.Length > 0)
                            rowParameters.Append(", ");

                        // create properties for lookup values
                        string rowValue = row[field.FieldName].ToString();
                        if (field.FieldType == "lookup" || field.FieldType == "int" || field.FieldType == "decimal")
                        {
                            if (String.IsNullOrEmpty(rowValue))
                                rowValue = "-1";
                            rowParameters.Append(rowValue);
                        }
                        else
                        {
                            rowValue = rowValue.Replace("\"", "\\\"");
                            rowParameters.Append(String.Format("\"{0}\"", rowValue));
                        }
                    }

                    var classValue = newClassInstanceFragment.WriteNewClassInstance(rowClassName, rowParameters.ToString());
                    rowList.AppendLine(localVariableFragment.WriteLocalVariable("row" + row["id"].ToString(), rowClassName, classValue));
                }

                // replace standard set of variables
                ReplaceVariables(fileContents, templateVariables);
                fileContents.Replace("PACKAGE_STRING", PackageString);
                fileContents.Replace("ROW_CLASS_NAME", rowClassName);
                fileContents.Replace("CLASS_NAME", className);
                fileContents.Replace("TABLE_NAME", table.TableName);
                fileContents.Replace("ROW_LIST", rowList.ToString().TrimEnd('\n', '\r'));

                // write the file
                var filePath = outputDirectory + "\\" + FOLDER + "\\" + fileName;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                StreamWriter writer = new StreamWriter(filePath);
                writer.Write(fileContents);
                writer.Close();

                Log("Created Table File - " + fileName);
            }
        }

        public void WriteDatabaseFile(string outputDirectory)
        {
            if(dataBaseFileTemplate == null)
                return;

            // check prerequisites
            if (newClassInstanceFragment == null)
            {
                Log("No class instance fragment available to write database template.");
                return;
            }
            if (localAssignmentFragment == null)
            {
                Log("No local assignment fragment available to write database template.");
            }

            string fileName = dataBaseFileTemplate.FileName;
            StringBuilder fileContents = new StringBuilder(dataBaseFileTemplate.FileContents);

            StringBuilder variableList = new StringBuilder();
            variableList.AppendLine("// code generated list of all tables");
            foreach (DataTable table in sourceDataSet.Tables)
            {
                var className = NameUtils.FormatClassName(table.TableName) + "Table";
                var classValue = newClassInstanceFragment.WriteNewClassInstance(className, "");
                var varName = NameUtils.FormatClassConstantName(table.TableName);
                variableList.AppendLine(classVariableFragment.WriteClassVariable(varName, className));
            }

            StringBuilder classList = new StringBuilder();
            classList.AppendLine("// code generated list of all tables");
            foreach (DataTable table in sourceDataSet.Tables)
            {
                var className = NameUtils.FormatClassName(table.TableName) + "Table";
                var classValue = newClassInstanceFragment.WriteNewClassInstance(className, "");
                var variableName = NameUtils.FormatClassConstantName(table.TableName);
                classList.AppendLine(localAssignmentFragment.WriteLocalAssignment(variableName, classValue));
            }

            // replace standard set of variables
            ReplaceVariables(fileContents, templateVariables);
            fileContents.Replace("VARIABLE_LIST", variableList.ToString().TrimEnd('\n', '\r'));
            fileContents.Replace("CLASS_LIST", classList.ToString().TrimEnd('\n', '\r'));
            fileContents.Replace("PACKAGE_STRING", PackageString);

            // write the file
            var filePath = outputDirectory + "\\" + dataBaseFileTemplate.FileName;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            StreamWriter writer = new StreamWriter(filePath);
            writer.Write(fileContents);
            writer.Close();

            Log("Created Database File - " + dataBaseFileTemplate.FileName);
        }

        public void WritePackageFiles(string outputDirectory)
        {
            string fileName;
            StringBuilder fileContents;
            StreamWriter writer;

            foreach (TemplateFile packageFileTemplate in packageFileTemplates)
            {
                // work out the file name
                fileName = packageFileTemplate.FileName;
                fileContents = new StringBuilder(packageFileTemplate.FileContents);

                // build the file contents
                ReplaceVariables(fileContents, templateVariables);
                fileContents.Replace("PACKAGE_STRING", PackageString);

                // write the file
                var filePath = outputDirectory + "\\" + fileName;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                writer = new StreamWriter(filePath);
                writer.Write(fileContents);
                writer.Close();

                Log("Created Package File - " + fileName);
            }
        }

        public static void ReplaceVariables(StringBuilder fileContents, List<KeyValuePair<string, string>> variables)
        {
            foreach (KeyValuePair<string, string> pair in variables)
            {
                fileContents.Replace(pair.Key, pair.Value);
            }
        }

        public void Log(string message)
        {
            var log = new KeyValuePair<DateTime, string>(DateTime.Now, message);
            logMessages.Add(log);
        }
    }
}
