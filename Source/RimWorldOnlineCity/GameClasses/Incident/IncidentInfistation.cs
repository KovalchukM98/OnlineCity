﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentInfistation : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, Current.Game.AnyPlayerHomeMap);
            parms.customLetterLabel = "test infistation";
            parms.customLetterText = "test infistation";
            parms.faction = null;
            parms.forced = true;  //игнорировать все условия для события
            parms.target = Find.CurrentMap;
            parms.points = StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap);

            if (!IncidentDefOf.Infestation.Worker.TryExecute(parms))
            {
                Messages.Message($"Failed_Test_quest", MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }
    }
}