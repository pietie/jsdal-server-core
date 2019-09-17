using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings.ObjectModel
{

    public enum RoutineIncludeExcludeInstructionSource
    {
        Unknown = 0,
        DatabaseMetadata = 10,
        DbSourceLevel = 20,
        JsFileLevel = 30
    }

    public class RoutineIncludeExcludeInstruction
    {
        public BaseRule Rule;
        public bool? Included;
        public bool? Excluded;
        public string Reason;
        public RoutineIncludeExcludeInstructionSource? Source;

        public static RoutineIncludeExcludeInstruction Create(CachedRoutine routine, List<BaseRule> appRules, DefaultRuleMode defaultRuleMode, List<BaseRule> fileRules = null)
        {
            var instruction = new RoutineIncludeExcludeInstruction();

            // apply Metadata first
            if (routine.jsDALMetadata != null && routine.jsDALMetadata.jsDAL != null)
            {
                if (routine.jsDALMetadata.jsDAL != null)
                {
                    if (routine.jsDALMetadata.jsDAL.exclude ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Excluded = routine.jsDALMetadata.jsDAL.exclude;
                        if (instruction.Excluded ?? false) instruction.Reason = "T-SQL metadata";
                    }
                    else if (!routine.jsDALMetadata.jsDAL.include ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Included = routine.jsDALMetadata.jsDAL.include;
                        if (instruction.Included ?? false) instruction.Reason = "T-SQL metadata";
                    }
                }
            }

            if (instruction.Reason != null) return instruction;

            // always apply APP-level rules first
            foreach (var rule in appRules)
            {
                if (rule.Apply(routine))
                {
                    if (defaultRuleMode == DefaultRuleMode.ExcludeAll)
                    {
                        instruction.Included = true;
                        instruction.Reason = rule.ToString();
                    }
                    else if (defaultRuleMode == DefaultRuleMode.IncludeAll)
                    {
                        instruction.Excluded = true;
                        instruction.Reason = rule.ToString();
                    }
                    else throw new Exception("Unsupported DefaultRuleMode: " + defaultRuleMode.ToString());

                    instruction.Rule = rule;
                    instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;

                    return instruction;
                }
            };

            if (instruction.Rule != null) return instruction;

            // apply JSFile level
            if (fileRules != null)
            {
                foreach (var fileRule in fileRules)
                {
                    if (fileRule == null) continue;

                    if (fileRule.Apply(routine))
                    {
                        if (defaultRuleMode == DefaultRuleMode.ExcludeAll)
                        {
                            instruction.Included = true;
                            instruction.Reason = fileRule.ToString(); // TODO: Consider recording a more substantial reference to the rule
                        }
                        else if (defaultRuleMode == DefaultRuleMode.IncludeAll)
                        {
                            instruction.Excluded = true;
                            instruction.Reason = fileRule.ToString();
                        }
                        else throw new Exception("Unsupported DefaultRuleMode: " + defaultRuleMode.ToString());

                        instruction.Rule = fileRule;
                        instruction.Source = RoutineIncludeExcludeInstructionSource.JsFileLevel;

                        return instruction;
                    }

                };

            }

            if (defaultRuleMode == DefaultRuleMode.ExcludeAll) instruction.Excluded = true;
            else instruction.Included = true;

            instruction.Rule = null;
            instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;
            instruction.Reason = "Default";

            return instruction;
        }

    }

    public class CachedRoutine
    {
        public string Schema;
        public string Routine;
        public string Type; //e.g. Proc, UDF or Table-valued function
        public long RowVer;
        public string ParametersHash;
        public List<RoutineParameterV2> Parameters;
        public long ResultSetRowver;
        public Dictionary<string/*TableName*/, List<ResultSetFieldMetadata>> ResultSetMetadata;
        public string ResultSetHash;
        public string ResultSetError;
        public jsDALMetadata jsDALMetadata;

        public bool IsDeleted;

        [JsonIgnore]
        public string FullName { get { return $"[{this.Schema}].[{this.Routine}]"; } }

        //!public RuleInstructions: { [id: string/*JsFile Guid*/]: RoutineIncludeExcludeInstruction }; // Dictionary<JsFile/*If null then DB-level*/, RoutineIncludeExcludeInstruction>;

        // TODO:  RuleInstructions require revisting. Don't believe it should live on the CachedRoutine itself. Rather each JsFile should calculate it's own list???
        [JsonIgnore]
        public Dictionary<JsFile/*If null then DB-level*/, RoutineIncludeExcludeInstruction> RuleInstructions;

        public CachedRoutine()
        {
            this.RuleInstructions = new Dictionary<JsFile, RoutineIncludeExcludeInstruction>();
        }

        /**public static createFromJson(rawJson: any): CachedRoutine {
                let cachedRoutine = new CachedRoutine();

cachedRoutine.Schema = rawJson.Schema;
                cachedRoutine.Routine = rawJson.Routine;
                cachedRoutine.Type = rawJson.Type;
                cachedRoutine.RowVer = rawJson.RowVer;

                cachedRoutine.ResultSetRowver = rawJson.ResultSetRowver;
                cachedRoutine.ResultSetError = rawJson.ResultSetError;
                cachedRoutine.IsDeleted = rawJson.IsDeleted;

                cachedRoutine.Parameters = rawJson.Parameters;

                cachedRoutine.jsDALMetadata = rawJson.jsDALMetadata;

                return cachedRoutine;
            }*/

        public bool Equals(CachedRoutine r)
        {
            return this.Schema.Equals(r.Schema, StringComparison.OrdinalIgnoreCase)
                && this.Routine.Equals(r.Routine, StringComparison.OrdinalIgnoreCase);

        }

        public bool Equals(string schema, string routineName)
        {
            if (schema.StartsWith('[') && schema.EndsWith(']')) schema = schema.Substring(1,schema.Length-2);
            if (routineName.StartsWith('[') && routineName.EndsWith(']')) routineName = routineName.Substring(1,routineName.Length-2);

            return this.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase)
            && this.Routine.Equals(routineName, StringComparison.OrdinalIgnoreCase);
        }

        public bool Contains(string txt)
        {
            string schema = null;

            if (txt.Contains('.'))
            {
                schema = txt.Substring(0, txt.IndexOf('.'));
                txt = txt.Substring(schema.Length + 1);

                return this.Schema.Contains(schema, StringComparison.OrdinalIgnoreCase) && this.Routine.Contains(txt, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return this.Schema.Contains(txt, StringComparison.OrdinalIgnoreCase) || this.Routine.Contains(txt, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EqualsQuery(string txt)
        {
            string schema = null;

            if (txt.Contains('.'))
            {
                schema = txt.Substring(0, txt.IndexOf('.'));
                txt = txt.Substring(schema.Length + 1);

                return this.Equals(schema, txt);
            }
            else
            {
                return this.FullName.Equals(txt, StringComparison.OrdinalIgnoreCase);
            }
        }


        public RoutineIncludeExcludeInstruction ApplyRules(Application app, JsFile jsFileContext)
        {
            var instruction = new RoutineIncludeExcludeInstruction();

            // apply Metadata first
            if (this.jsDALMetadata != null && this.jsDALMetadata.jsDAL != null)
            {
                if (this.jsDALMetadata.jsDAL != null)
                {
                    if (this.jsDALMetadata.jsDAL.exclude ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Excluded = this.jsDALMetadata.jsDAL.exclude;
                        if (instruction.Excluded ?? false) instruction.Reason = "T-SQL metadata";
                    }
                    else if (!this.jsDALMetadata.jsDAL.include ?? false)
                    {
                        instruction.Source = RoutineIncludeExcludeInstructionSource.DatabaseMetadata;
                        instruction.Included = this.jsDALMetadata.jsDAL.include;
                        if (instruction.Included ?? false) instruction.Reason = "T-SQL metadata";
                    }
                }
            }

            if (instruction.Reason != null) return instruction;

            // apply DB source level
            foreach (var dbRule in app.Rules)
            {

                if (dbRule == null) continue;

                if (dbRule.Apply(this))
                {
                    if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll)
                    {
                        instruction.Included = true;
                        instruction.Reason = dbRule.ToString();
                    }
                    else if (app.DefaultRuleMode == (int)DefaultRuleMode.IncludeAll)
                    {
                        instruction.Excluded = true;
                        instruction.Reason = dbRule.ToString();
                    }
                    else throw new Exception("Unsupported DefaultRuleMode: " + app.DefaultRuleMode);

                    instruction.Rule = dbRule;
                    instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;

                    return instruction;
                }
            };



            if (instruction.Rule != null) return instruction;

            // apply JSFile level
            if (jsFileContext != null)
            {

                foreach (var fileRule in jsFileContext.Rules)
                {
                    if (fileRule == null) continue;

                    if (fileRule.Apply(this))
                    {
                        if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll)
                        {
                            instruction.Included = true;
                            instruction.Reason = fileRule.ToString(); // TODO: Consider recording a more substantial reference to the rule
                        }
                        else if (app.DefaultRuleMode == (int)DefaultRuleMode.IncludeAll)
                        {
                            instruction.Excluded = true;
                            instruction.Reason = fileRule.ToString();
                        }
                        else throw new Exception("Unsupported DefaultRuleMode: " + app.DefaultRuleMode);

                        instruction.Rule = fileRule;
                        instruction.Source = RoutineIncludeExcludeInstructionSource.JsFileLevel;

                        return instruction;
                    }

                };

            }

            if (app.DefaultRuleMode == (int)DefaultRuleMode.ExcludeAll) instruction.Excluded = true;
            else instruction.Included = true;

            instruction.Rule = null;
            instruction.Source = RoutineIncludeExcludeInstructionSource.DbSourceLevel;
            instruction.Reason = "Default";
            return instruction;
        }


    }
}
