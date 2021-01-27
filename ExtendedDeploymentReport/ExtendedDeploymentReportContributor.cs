using System;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.SqlServer.Dac.Deployment;
using Microsoft.SqlServer.Dac.Extensibility;
using Microsoft.SqlServer.Dac.Model;

namespace ExtendedDeploymentReportContributor
{
    [ExportDeploymentPlanExecutor("ExtendedDeploymentReportContributor.ExtendedDeploymentReportContributor", "1.0.0.0")]
    public class ExtendedDeploymentReportContributor : DeploymentPlanExecutor
    {

        public const string GenerateExtendedReport = "ExtendedDeploymentReportContributor.GenerateExtendedReport";

        /// <summary>
        /// Override the OnExecute method to perform actions when you execute the deployment plan for
        /// a database project.
        /// </summary>
        protected override void OnExecute(DeploymentPlanContributorContext context)
        {
            // determine whether the user specified a report is to be generated
            bool generateReport = false;
            string generateReportValue;
            if (context.Arguments.TryGetValue(GenerateExtendedReport, out generateReportValue) == false)
            {
                // couldn't find the GenerateUpdateReport argument, so do not generate
                generateReport = false;
            }
            else
            {
                // GenerateUpdateReport argument was specified, try to parse the value
                if (bool.TryParse(generateReportValue, out generateReport))
                {
                    // if we end up here, the value for the argument was not valid.
                    // default is false, so do nothing.
                }
            }

            if (generateReport == false)
            {
                // if user does not want to generate a report, we are done
                return;
            }

            // We will output to the same directory where the deployment script
            // is output or to the current directory
            string reportPrefix = context.Options.TargetDatabaseName;
            string reportPath;
            if (string.IsNullOrEmpty(context.DeploymentScriptPath))
            {
                reportPath = Environment.CurrentDirectory;
            }
            else
            {
                reportPath = Path.GetDirectoryName(context.DeploymentScriptPath);
            }
            FileInfo summaryReportFile = new FileInfo(Path.Combine(reportPath, reportPrefix + ".summary.xml"));
            FileInfo detailsReportFile = new FileInfo(Path.Combine(reportPath, reportPrefix + ".details.xml"));

            // Generate the reports by using the helper class DeploymentReportWriter
            DeploymentReportWriter writer = new DeploymentReportWriter(context);
            writer.WriteReport(summaryReportFile);
            writer.IncludeScripts = true;
            writer.WriteReport(detailsReportFile);

            string msg = "Deployment reports ->"
                + Environment.NewLine + summaryReportFile.FullName
                + Environment.NewLine + detailsReportFile.FullName;

            ExtensibilityError reportMsg = new ExtensibilityError(msg, Severity.Message);
            base.PublishMessage(reportMsg);
        }

        /// <summary>
        /// This class is used to generate a deployment
        /// report. 
        /// </summary>
        private class DeploymentReportWriter
        {
            readonly TSqlModel _sourceModel;
            readonly ModelComparisonResult _diff;
            readonly DeploymentStep _planHead;

            /// <summary>
            /// The constructor accepts the same context info
            /// that was passed to the OnExecute method of the
            /// deployment contributor.
            /// </summary>
            public DeploymentReportWriter(DeploymentPlanContributorContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException("context");
                }

                // save the source model, source/target differences,
                // and the beginning of the deployment plan.
                _sourceModel = context.Source;
                _diff = context.ComparisonResult;
                _planHead = context.PlanHandle.Head;
            }
            /// <summary>
            /// Property indicating whether script bodies
            /// should be included in the report.
            /// </summary>
            public bool IncludeScripts { get; set; }

            /// <summary>
            /// Drives the report generation, opening files, 
            /// writing the beginning and ending report elements,
            /// and calling helper methods to report on the
            /// plan operations.
            /// </summary>
            internal void WriteReport(FileInfo reportFile)
            {// Assumes that we have a valid report file
                if (reportFile == null)
                {
                    throw new ArgumentNullException("reportFile");
                }

                // set up the XML writer
                XmlWriterSettings xmlws = new XmlWriterSettings();
                // Indentation makes it a bit more readable
                xmlws.Indent = true;
                FileStream fs = new FileStream(reportFile.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                XmlWriter xmlw = XmlWriter.Create(fs, xmlws);

                try
                {
                    xmlw.WriteStartDocument(true);
                    xmlw.WriteStartElement("DeploymentReport");

                    // Summary report of the operations that
                    // are contained in the plan.
                    ReportPlanOperations(xmlw);

                    // You could add a method call here
                    // to produce a detailed listing of the 
                    // differences between the source and
                    // target model.
                    xmlw.WriteEndElement();
                    xmlw.WriteEndDocument();
                    xmlw.Flush();
                    fs.Flush();
                }
                finally
                {
                    xmlw.Close();
                    fs.Dispose();
                }
            }

            /// <summary>
            /// Writes details for the various operation types
            /// that could be contained in the deployment plan.
            /// Optionally writes script bodies, depending on
            /// the value of the IncludeScripts property.
            /// </summary>
            private void ReportPlanOperations(XmlWriter xmlw)
            {// write the node to indicate the start
                // of the list of operations.
                xmlw.WriteStartElement("Operations");

                // Loop through the steps in the plan,
                // starting at the beginning.
                DeploymentStep currentStep = _planHead;
                while (currentStep != null)
                {
                    // Report the type of step
                    xmlw.WriteStartElement(currentStep.GetType().Name);

                    // based on the type of step, report
                    // the relevant information.
                    // Note that this procedure only handles 
                    // a subset of all step types.
                    if (currentStep is SqlRenameStep)
                    {
                        SqlRenameStep renameStep = (SqlRenameStep)currentStep;
                        xmlw.WriteAttributeString("OriginalName", renameStep.OldName);
                        xmlw.WriteAttributeString("NewName", renameStep.NewName);
                        xmlw.WriteAttributeString("Category", GetElementCategory(renameStep.RenamedElement));
                    }
                    else if (currentStep is SqlMoveSchemaStep)
                    {
                        SqlMoveSchemaStep moveStep = (SqlMoveSchemaStep)currentStep;
                        xmlw.WriteAttributeString("OrignalName", moveStep.PreviousName);
                        xmlw.WriteAttributeString("NewSchema", moveStep.NewSchema);
                        xmlw.WriteAttributeString("Category", GetElementCategory(moveStep.MovedElement));
                    }
                    else if (currentStep is SqlTableMigrationStep)
                    {
                        SqlTableMigrationStep dmStep = (SqlTableMigrationStep)currentStep;
                        xmlw.WriteAttributeString("Name", GetElementName(dmStep.SourceTable));
                        xmlw.WriteAttributeString("Category", GetElementCategory(dmStep.SourceElement));
                    }
                    else if (currentStep is CreateElementStep)
                    {
                        CreateElementStep createStep = (CreateElementStep)currentStep;
                        xmlw.WriteAttributeString("Name", GetElementName(createStep.SourceElement));
                        xmlw.WriteAttributeString("Category", GetElementCategory(createStep.SourceElement));
                    }
                    else if (currentStep is AlterElementStep)
                    {
                        AlterElementStep alterStep = (AlterElementStep)currentStep;
                        xmlw.WriteAttributeString("Name", GetElementName(alterStep.SourceElement));
                        xmlw.WriteAttributeString("Category", GetElementCategory(alterStep.SourceElement));
                    }
                    else if (currentStep is DropElementStep)
                    {
                        DropElementStep dropStep = (DropElementStep)currentStep;
                        xmlw.WriteAttributeString("Name", GetElementName(dropStep.TargetElement));
                        xmlw.WriteAttributeString("Category", GetElementCategory(dropStep.TargetElement));
                    }

                    // If the script bodies are to be included,
                    // add them to the report.
                    if (this.IncludeScripts)
                    {
                        using (StringWriter sw = new StringWriter())
                        {
                            currentStep.GenerateBatchScript(sw);
                            string tsqlBody = sw.ToString();
                            if (string.IsNullOrEmpty(tsqlBody) == false)
                            {
                                xmlw.WriteCData(tsqlBody);
                            }
                        }
                    }

                    // close off the current step
                    xmlw.WriteEndElement();
                    currentStep = currentStep.Next;
                }
                xmlw.WriteEndElement();
            }

            /// <summary>
            /// Returns the category of the specified element
            /// in the source model
            /// </summary>
            private string GetElementCategory(TSqlObject element)
            {
                return element.ObjectType.Name;
            }

            /// <summary>
            /// Returns the name of the specified element
            /// in the source model
            /// </summary>
            private static string GetElementName(TSqlObject element)
            {
                StringBuilder name = new StringBuilder();
                if (element.Name.HasExternalParts)
                {
                    foreach (string part in element.Name.ExternalParts)
                    {
                        if (name.Length > 0)
                        {
                            name.Append('.');
                        }
                        name.AppendFormat("[{0}]", part);
                    }
                }

                foreach (string part in element.Name.Parts)
                {
                    if (name.Length > 0)
                    {
                        name.Append('.');
                    }
                    name.AppendFormat("[{0}]", part);
                }

                return name.ToString();
            }
        }        


    }
}
