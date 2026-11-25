namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;

    public sealed partial class ExerciseCatalogService
    {
        private static List<PropertyCatalogDescriptorProfile> DescriptorProfiles(PropertyLibrarySection config)=>config.CatalogDescriptorProfiles.Select(p=>new PropertyCatalogDescriptorProfile{ProgramKey=p.ProgramKey,FamilyLabel=p.FamilyLabel,Families=p.Families,AttireLabel=p.AttireLabel,Attires=p.Attires,LevelLabel=p.LevelLabel,Levels=p.Levels,NotationSystems=p.NotationSystems,NameLanguages=p.NameLanguages.Select(o=>new PropertyOptionSeed{Key=CatalogLanguageTags.Normalize(o.Key)!,Label=o.Label}).ToList()}).ToList();
        private static void ValidateDescriptorFilters(EnumerationQuery query)
        {
            string? language=query.FilterString("nameLanguage");if(language!=null&&CatalogLanguageTags.Normalize(language)==null)throw ApiErrorException.Field("nameLanguage","Use language[-Script][-REGION].");
            string? family=query.FilterString("familyKey");if(family!=null&&(family.Length<1||family.Length>64))throw ApiErrorException.Field("familyKey","Use 1 to 64 characters.");
            foreach(string key in new[]{"attireKey","levelKey","notationSystemKey"}){string? value=query.FilterString(key);if(value!=null&&(value.Length<1||value.Length>64))throw ApiErrorException.Field(key,"Use 1 to 64 characters.");}
            if(query.Search!=null)query.Search=query.Search.Normalize(NormalizationForm.FormC);
        }
        private static string? CleanDescriptor(string? value)=>String.IsNullOrWhiteSpace(value)?null:value.Trim();
        private static void ValidateModalityDetails(CatalogEntry entry,PropertyLibrarySection config,Dictionary<string,string> errors)
        {
            CatalogModalityDetails details=entry.ModalityDetails??new CatalogModalityDetails();entry.ModalityDetails=details;
            details.FamilyKey=String.IsNullOrWhiteSpace(details.FamilyKey)?null:details.FamilyKey.Trim();
            PropertyCatalogDescriptorProfile? profile=config.CatalogDescriptorProfiles.FirstOrDefault(p=>p.ProgramKey==entry.Program);
            if(details.FamilyKey!=null&&(details.FamilyKey.Length>64||!(profile?.Families.Any(o=>o.Key==details.FamilyKey)??false)))errors["modalityDetails.familyKey"]="Use a configured family for this program.";
            details.AttireKey=CleanDescriptor(details.AttireKey);details.LevelKey=CleanDescriptor(details.LevelKey);details.NotationSystemKey=CleanDescriptor(details.NotationSystemKey);details.NotationText=CleanDescriptor(details.NotationText)?.Normalize(NormalizationForm.FormC);
            if(details.AttireKey!=null&&!(profile?.Attires.Any(o=>o.Key==details.AttireKey)??false))errors["modalityDetails.attireKey"]="Use a configured attire for this program.";
            if(details.LevelKey!=null&&!(profile?.Levels.Any(o=>o.Key==details.LevelKey)??false))errors["modalityDetails.levelKey"]="Use a configured instructional level for this program.";
            if(details.NotationSystemKey!=null&&!(profile?.NotationSystems.Any(o=>o.Key==details.NotationSystemKey)??false))errors["modalityDetails.notationSystemKey"]="Use a configured notation system for this program.";
            if((details.NotationText==null)!=(details.NotationSystemKey==null))errors["modalityDetails.notationText"]="Provide notation text and its configured system together.";
            if(details.NotationText?.Length>500)errors["modalityDetails.notationText"]="Use 1 to 500 characters.";
            if(details.LocalizedNames==null||details.LocalizedNames.Count>10){errors["modalityDetails.localizedNames"]="Provide 0 to 10 localized names.";return;}
            HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<details.LocalizedNames.Count;i++)
            {
                CatalogLocalizedName name=details.LocalizedNames[i];string field="modalityDetails.localizedNames["+i+"]";
                if(name==null){errors[field]="A localized name is required.";continue;}
                string? language=CatalogLanguageTags.Normalize(name.LanguageTag);
                if(language==null||!(profile?.NameLanguages.Any(o=>CatalogLanguageTags.Normalize(o.Key)==language)??false))errors[field+".languageTag"]="Choose a configured language for this program.";
                name.Text=(name.Text??String.Empty).Normalize(NormalizationForm.FormC).Trim();
                if(name.Text.Length<1||name.Text.Length>160)errors[field+".text"]="Use 1 to 160 characters.";
                if(language!=null){name.LanguageTag=language;if(!names.Add(language+"\n"+name.Text))errors[field]="Duplicate localized names are not allowed.";}
            }
        }
    }
}
