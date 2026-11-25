namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    public sealed partial class ExerciseCatalogService
    {
        private static void ValidateApparatus(CatalogEntry entry,PropertyLibrarySection config,Dictionary<string,string> errors)
        {
            CatalogApparatusSetup setup=entry.ApparatusSetup??new CatalogApparatusSetup();entry.ApparatusSetup=setup;
            setup.ApparatusKey=CleanDescriptor(setup.ApparatusKey);
            setup.Manufacturer=CleanDescriptor(setup.Manufacturer)?.Normalize(NormalizationForm.FormC);
            setup.Model=CleanDescriptor(setup.Model)?.Normalize(NormalizationForm.FormC);
            setup.SetupText=CleanDescriptor(setup.SetupText)?.Normalize(NormalizationForm.FormC);
            setup.SpringSetupText=CleanDescriptor(setup.SpringSetupText)?.Normalize(NormalizationForm.FormC);
            if(setup.ApparatusKey!=null&&(setup.ApparatusKey.Length>64||!config.CatalogEquipment.Any(o=>o.Key==setup.ApparatusKey)||!entry.Equipment.Contains(setup.ApparatusKey,StringComparer.Ordinal)))errors["apparatusSetup.apparatusKey"]="Choose configured equipment also selected on this entry.";
            if(setup.ApparatusKey==null&&(setup.Manufacturer!=null||setup.Model!=null||setup.SetupText!=null||setup.SpringSetupText!=null))errors["apparatusSetup.apparatusKey"]="Setup context requires an apparatus.";
            if(setup.Manufacturer?.Length>120)errors["apparatusSetup.manufacturer"]="Use at most 120 characters.";
            if(setup.Model?.Length>120)errors["apparatusSetup.model"]="Use at most 120 characters.";
            if(setup.SetupText?.Length>500)errors["apparatusSetup.setupText"]="Use at most 500 characters.";
            if(setup.SpringSetupText?.Length>500)errors["apparatusSetup.springSetupText"]="Use at most 500 characters.";
            if(setup.Model!=null&&setup.Manufacturer==null)errors["apparatusSetup.manufacturer"]="A model requires its manufacturer.";
            if(setup.SpringSetupText!=null&&(setup.ApparatusKey==null||setup.Manufacturer==null||setup.Model==null))errors["apparatusSetup.springSetupText"]="Spring setup requires the apparatus, manufacturer and model together.";
        }
    }
}
