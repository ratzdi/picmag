// MIT License
//
// Copyright (c) 2025 Dimitri Ratz
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace picmag
{
    partial class Program
    {
        private enum CommandType
        {
            Help,
            Version,
            FindDuplicates,
            Import,
            SanityChecks,
            MigrateCache,
            QualityReview,
            QualityScanExisting,
            ScheduleImport,
            UnscheduleImport
        }

        private enum SchedulePeriod
        {
            Daily,
            Weekly
        }

        private class CommandRequest
        {
            public CommandType Type { get; set; }
            public string DbFilepath { get; set; }
            public string ResultFilepath { get; set; }
            public string SourcePath { get; set; }
            public string TargetPath { get; set; }
            public List<string> Extensions { get; set; } = new List<string> { "jpg", "mp4" };
            public bool DeleteSourceAfterImport { get; set; }
            public bool DryRun { get; set; } = true;
            public QualityFilterMode QualityFilterMode { get; set; } = QualityFilterMode.Off;
            public bool WriteQualityReport { get; set; }
            public QualityReviewVerdict QualityReviewVerdict { get; set; } = QualityReviewVerdict.Reject;
            public QualityReviewAction QualityReviewAction { get; set; } = QualityReviewAction.List;
            public bool QualityScanOnlyMissing { get; set; } = true;
            public SchedulePeriod SchedulePeriod { get; set; } = SchedulePeriod.Daily;
            public string ScheduleTime { get; set; } = "02:00";
            public string ScheduleWeekday { get; set; } = "Mon";
        }

        void Start(string[] args)
        {
            if (!TryParseCommand(args, out var request))
            {
                PrintUsage();
                return;
            }

            ExecuteCommand(request);
        }

        bool TryParseCommand(string[] args, out CommandRequest request)
        {
            request = null;

            if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
            {
                request = new CommandRequest { Type = CommandType.Version };
                return true;
            }

            if (args.Length == 0 || args[0] == "-h")
            {
                request = new CommandRequest { Type = CommandType.Help };
                return true;
            }

            switch (args[0])
            {
                case "-d":
                    if (args.Length != 3)
                        return false;

                    request = new CommandRequest
                    {
                        Type = CommandType.FindDuplicates,
                        DbFilepath = args[1],
                        ResultFilepath = args[2]
                    };
                    return true;

                case "-i":
                    return TryParseImportCommand(args, out request);

                case "--sanity-checks":
                    return TryParseSanityChecksCommand(args, out request);

                case "--migrate-cache":
                    if (args.Length != 2)
                        return false;

                    request = new CommandRequest
                    {
                        Type = CommandType.MigrateCache,
                        TargetPath = args[1]
                    };
                    return true;

                case "--quality-review":
                    return TryParseQualityReviewCommand(args, out request);

                case "--quality-scan-existing":
                    return TryParseQualityScanExistingCommand(args, out request);

                case "--schedule-import":
                    return TryParseScheduleImportCommand(args, out request);

                case "--unschedule-import":
                    if (args.Length != 1)
                        return false;

                    request = new CommandRequest
                    {
                        Type = CommandType.UnscheduleImport
                    };
                    return true;

                default:
                    return false;
            }
        }

        bool TryParseScheduleImportCommand(string[] args, out CommandRequest request)
        {
            request = null;
            if (args.Length < 6)
                return false;

            var parsed = new CommandRequest
            {
                Type = CommandType.ScheduleImport,
                SourcePath = args[1],
                TargetPath = args[2],
                SchedulePeriod = SchedulePeriod.Daily,
                ScheduleTime = "02:00",
                ScheduleWeekday = "Mon"
            };

            var hasCustomExtensions = false;
            bool hasPeriod = false;

            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--delete-source")
                {
                    parsed.DeleteSourceAfterImport = true;
                }
                else if (args[i] == "--quality-report")
                {
                    parsed.WriteQualityReport = true;
                }
                else if (args[i] == "--quality-filter")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseQualityFilterMode(args[i + 1], out var qualityFilterMode))
                        return false;

                    parsed.QualityFilterMode = qualityFilterMode;
                    i++;
                }
                else if (args[i] == "--period")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseSchedulePeriod(args[i + 1], out var period))
                        return false;

                    parsed.SchedulePeriod = period;
                    hasPeriod = true;
                    i++;
                }
                else if (args[i] == "--time")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!IsValidHourMinute(args[i + 1]))
                        return false;

                    parsed.ScheduleTime = args[i + 1];
                    i++;
                }
                else if (args[i] == "--weekday")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseScheduleWeekday(args[i + 1], out var weekday))
                        return false;

                    parsed.ScheduleWeekday = weekday;
                    i++;
                }
                else if (args[i].StartsWith("-"))
                {
                    return false;
                }
                else
                {
                    if (hasCustomExtensions)
                        return false;

                    parsed.Extensions = new List<string>(args[i].ToLower().Split(','));
                    hasCustomExtensions = true;
                }
            }

            if (!hasPeriod)
                return false;

            request = parsed;
            return true;
        }

        bool TryParseSchedulePeriod(string rawValue, out SchedulePeriod period)
        {
            period = SchedulePeriod.Daily;

            if (string.Equals(rawValue, "daily", StringComparison.OrdinalIgnoreCase))
            {
                period = SchedulePeriod.Daily;
                return true;
            }

            if (string.Equals(rawValue, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                period = SchedulePeriod.Weekly;
                return true;
            }

            return false;
        }

        bool TryParseScheduleWeekday(string rawValue, out string weekday)
        {
            weekday = null;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "mon":
                case "monday":
                    weekday = "Mon";
                    return true;
                case "tue":
                case "tues":
                case "tuesday":
                    weekday = "Tue";
                    return true;
                case "wed":
                case "wednesday":
                    weekday = "Wed";
                    return true;
                case "thu":
                case "thur":
                case "thurs":
                case "thursday":
                    weekday = "Thu";
                    return true;
                case "fri":
                case "friday":
                    weekday = "Fri";
                    return true;
                case "sat":
                case "saturday":
                    weekday = "Sat";
                    return true;
                case "sun":
                case "sunday":
                    weekday = "Sun";
                    return true;
                default:
                    return false;
            }
        }

        bool IsValidHourMinute(string rawValue)
        {
            return DateTime.TryParseExact(rawValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        bool TryParseQualityReviewCommand(string[] args, out CommandRequest request)
        {
            request = null;
            if (args.Length < 2)
                return false;

            var parsed = new CommandRequest
            {
                Type = CommandType.QualityReview,
                TargetPath = args[1],
                QualityReviewVerdict = QualityReviewVerdict.Reject,
                QualityReviewAction = QualityReviewAction.List,
                DryRun = true
            };

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--verdict")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseQualityReviewVerdict(args[i + 1], out var verdict))
                        return false;

                    parsed.QualityReviewVerdict = verdict;
                    i++;
                }
                else if (args[i] == "--action")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseQualityReviewAction(args[i + 1], out var action))
                        return false;

                    parsed.QualityReviewAction = action;
                    i++;
                }
                else if (args[i] == "--apply-changes")
                {
                    parsed.DryRun = false;
                }
                else if (args[i] == "--dry-run")
                {
                    parsed.DryRun = true;
                }
                else
                {
                    return false;
                }
            }

            request = parsed;
            return true;
        }

        bool TryParseQualityScanExistingCommand(string[] args, out CommandRequest request)
        {
            request = null;
            if (args.Length < 2)
                return false;

            var parsed = new CommandRequest
            {
                Type = CommandType.QualityScanExisting,
                TargetPath = args[1],
                DryRun = true,
                QualityScanOnlyMissing = true
            };

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--apply-changes")
                {
                    parsed.DryRun = false;
                }
                else if (args[i] == "--dry-run")
                {
                    parsed.DryRun = true;
                }
                else if (args[i] == "--all")
                {
                    parsed.QualityScanOnlyMissing = false;
                }
                else if (args[i] == "--only-missing")
                {
                    parsed.QualityScanOnlyMissing = true;
                }
                else
                {
                    return false;
                }
            }

            request = parsed;
            return true;
        }

        bool TryParseQualityReviewVerdict(string rawValue, out QualityReviewVerdict verdict)
        {
            verdict = QualityReviewVerdict.Reject;

            if (string.Equals(rawValue, "review", StringComparison.OrdinalIgnoreCase))
            {
                verdict = QualityReviewVerdict.Review;
                return true;
            }

            if (string.Equals(rawValue, "reject", StringComparison.OrdinalIgnoreCase))
            {
                verdict = QualityReviewVerdict.Reject;
                return true;
            }

            return false;
        }

        bool TryParseQualityReviewAction(string rawValue, out QualityReviewAction action)
        {
            action = QualityReviewAction.List;

            if (string.Equals(rawValue, "list", StringComparison.OrdinalIgnoreCase))
            {
                action = QualityReviewAction.List;
                return true;
            }

            if (string.Equals(rawValue, "delete", StringComparison.OrdinalIgnoreCase))
            {
                action = QualityReviewAction.Delete;
                return true;
            }

            if (string.Equals(rawValue, "interactive", StringComparison.OrdinalIgnoreCase))
            {
                action = QualityReviewAction.Interactive;
                return true;
            }

            return false;
        }

        bool TryParseImportCommand(string[] args, out CommandRequest request)
        {
            request = null;
            if (args.Length < 3)
                return false;

            var parsed = new CommandRequest
            {
                Type = CommandType.Import,
                SourcePath = args[1],
                TargetPath = args[2]
            };

            var hasCustomExtensions = false;
            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--delete-source")
                {
                    parsed.DeleteSourceAfterImport = true;
                }
                else if (args[i] == "--quality-report")
                {
                    parsed.WriteQualityReport = true;
                }
                else if (args[i] == "--quality-filter")
                {
                    if (i + 1 >= args.Length)
                        return false;

                    if (!TryParseQualityFilterMode(args[i + 1], out var qualityFilterMode))
                        return false;

                    parsed.QualityFilterMode = qualityFilterMode;
                    i++;
                }
                else if (args[i].StartsWith("-"))
                {
                    return false;
                }
                else
                {
                    if (hasCustomExtensions)
                        return false;

                    parsed.Extensions = new List<string>(args[i].ToLower().Split(','));
                    hasCustomExtensions = true;
                }
            }

            request = parsed;
            return true;
        }

        bool TryParseQualityFilterMode(string rawValue, out QualityFilterMode qualityFilterMode)
        {
            qualityFilterMode = QualityFilterMode.Off;

            if (string.Equals(rawValue, "off", StringComparison.OrdinalIgnoreCase))
            {
                qualityFilterMode = QualityFilterMode.Off;
                return true;
            }

            if (string.Equals(rawValue, "warn", StringComparison.OrdinalIgnoreCase))
            {
                qualityFilterMode = QualityFilterMode.Warn;
                return true;
            }

            if (string.Equals(rawValue, "strict", StringComparison.OrdinalIgnoreCase))
            {
                qualityFilterMode = QualityFilterMode.Strict;
                return true;
            }

            return false;
        }

        bool TryParseSanityChecksCommand(string[] args, out CommandRequest request)
        {
            request = null;
            if (args.Length < 2)
                return false;

            var parsed = new CommandRequest
            {
                Type = CommandType.SanityChecks,
                TargetPath = args[1],
                DryRun = true
            };

            var hasCustomExtensions = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--apply-changes")
                {
                    parsed.DryRun = false;
                }
                else if (args[i] == "--dry-run")
                {
                    parsed.DryRun = true;
                }
                else if (args[i].StartsWith("-"))
                {
                    return false;
                }
                else
                {
                    if (hasCustomExtensions)
                        return false;

                    parsed.Extensions = new List<string>(args[i].ToLower().Split(','));
                    hasCustomExtensions = true;
                }
            }

            request = parsed;
            return true;
        }
    }
}
