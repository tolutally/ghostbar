using System.Collections.Generic;

namespace GhostBar
{
    public class PromptTemplate
    {
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";
        
        public override string ToString() => Name;
    }

    public static class PromptTemplates
    {
        public static List<PromptTemplate> All { get; } = new List<PromptTemplate>
        {
            new PromptTemplate 
            { 
                Name = "General Summary", 
                Prompt = @"You are an expert meeting notetaker. Analyze the transcript and provide:
1. An Executive Summary (2-3 sentences).
2. Key Discussion Points (bullet points).
3. Action Items with assignees." 
            },
            new PromptTemplate 
            { 
                Name = "Action Items Only", 
                Prompt = @"Extract only the Action Items from this transcript. 
Format as a checklist:
- [ ] Task (Assignee) - Context" 
            },
            new PromptTemplate 
            { 
                Name = "User Interview", 
                Prompt = @"Analyze this user interview transcript.
Identify:
1. User Pain Points.
2. Feature Requests / Desires.
3. Positive Feedback.
4. Direct Quotes (sentiment analysis)." 
            },
            new PromptTemplate 
            { 
                Name = "Code/Bug Report", 
                Prompt = @"This is a technical discussion. Extract:
1. The bug or issue described.
2. The steps to reproduce (if mentioned).
3. Proposed solutions or next steps.
4. Code snippets or specific files mentioned." 
            }
        };
    }
}
