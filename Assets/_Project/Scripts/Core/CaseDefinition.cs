using System;
using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>A selectable suspect on the evidence board.</summary>
    [Serializable]
    public class SuspectInfo
    {
        public string id;
        public string displayName;
    }

    /// <summary>
    /// Static data for one case (X or Y), including the correct answer.
    /// Create via Assets > Create > FYP > Case Definition, one asset per case.
    /// </summary>
    [CreateAssetMenu(fileName = "CaseDefinition", menuName = "FYP/Case Definition")]
    public class CaseDefinition : ScriptableObject
    {
        [SerializeField] private CaseId caseId = CaseId.X;
        [SerializeField] private string title = "Untitled case";
        [SerializeField] private List<SuspectInfo> suspects = new List<SuspectInfo>();

        [Header("Correct answer")]
        [SerializeField] private string correctSuspectId = "";
        [Tooltip("Clue ids that must be submitted as evidence. Must match Clue.ClueId values in the scene.")]
        [SerializeField] private List<string> keyEvidenceIds = new List<string>();

        [TextArea(3, 10)]
        [SerializeField] private string experimenterNotes = "";

        public CaseId CaseId => caseId;
        public string Title => title;
        public IReadOnlyList<SuspectInfo> Suspects => suspects;
        public string CorrectSuspectId => correctSuspectId;
        public IReadOnlyList<string> KeyEvidenceIds => keyEvidenceIds;
        public string ExperimenterNotes => experimenterNotes;

        public AnswerResult Check(string submittedSuspectId, IEnumerable<string> submittedEvidenceIds)
        {
            return AnswerChecker.Check(correctSuspectId, keyEvidenceIds, submittedSuspectId, submittedEvidenceIds);
        }
    }
}
