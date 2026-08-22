export const EmailStrings = {
  brand: {
    name: 'Zextri',
    logoAlt: 'Zextri logo'
  },
  sender: {
    email: 'hello@zextri.com',
    displayName: 'Shreya from Zextri',
    name: 'Shreya Singh',
    title: 'Founder, Zextri',
    testInboxName: 'Zextri Test Inbox'
  },
  urls: {
    logo: 'https://zextri.com/icons/zextri-192.png',
    website: 'https://zextri.com',
    unsubscribe: 'https://zextri.com/unsubscribe',
    demo: 'https://www.youtube.com/watch?v=Zo66nD5CMsc',
    relationshipDemo: 'https://www.youtube.com/watch?v=v_bxGZnQU5o',
    followUpDemo: 'https://www.youtube.com/watch?v=LOfbyaqfk3w',
    chrome: 'https://chromewebstore.google.com/detail/zextri/jnfghdnpnebjlokdgfdfioncdpbmdiba',
    edge: 'https://microsoftedge.microsoft.com/addons/detail/zextri/lopeokgklkpknnflmmgkaefnmphjielc'
  },
  templates: {
    shared: {
      companyFallback: 'your organization',
      greeting: 'Hi',
      ctaWatchDemo: 'Watch Demo',
      ctaChrome: 'Add to Chrome',
      ctaEdge: 'Get for Microsoft Edge',
      storeAvailability: 'Available on the official Chrome and Microsoft Edge stores.',
      signatureClosing: 'Warm regards,',
      signatureWebsiteLabel: 'Visit Zextri',
      unsubscribeLabel: 'Unsubscribe',
      unsubscribeSuffix: ' from future outreach.'
    },
    quickIntroduction: {
      name: 'Quick Introduction',
      description: 'Personal, minimal founder-led cold outreach.',
      category: 'Cold outreach',
      intendedUse: 'Use for a first touch with a relevant lead.',
      defaultSubject: 'A quick idea for {{company}}',
      fallbackOpening: 'Hi there,\n\nI thought Zextri could be useful for your growth workflow.',
      fallbackBodyRest: "Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.\n\nWould you be open to a quick look? I'd be happy to send a short demo.",
      opening: 'I came across your work at {{company}} and thought Zextri could be useful for your growth workflow.',
      body: 'Zextri helps teams identify relationships that need attention and write timely, context-aware follow-ups across LinkedIn and X.',
      plainTextQuestion: "Would you be open to a quick look? I'd be happy to send a short demo.",
      question: 'Would you be open to a quick look?'
    },
    relationshipValue: {
      name: 'Relationship Value',
      description: "Explains Zextri's relationship-signal value.",
      category: 'Cold outreach',
      intendedUse: 'Use when the lead may care about follow-up quality and relationship context.',
      defaultSubject: 'Relationship context for {{company}}',
      opening: 'I noticed {{company}} while reviewing teams where relationship context and timely follow-up can make a meaningful difference.',
      signalEyebrow: 'Relationship signal',
      signalHeading: 'Know who needs attention, then write with context.',
      signalBody: 'Zextri keeps relationship memory and follow-up timing visible across LinkedIn and X.',
      question: 'Would a short overview be useful?'
    },
    demoFollowUp: {
      name: 'Demo Follow-up',
      description: 'Short follow-up for a prospect who has shown interest.',
      category: 'Follow-up',
      intendedUse: 'Use after a conversation, demo, or prior interest.',
      defaultSubject: 'Zextri demo for {{company}}',
      opening: 'Following up with a short Zextri demo for {{company}}.',
      body: 'The demo shows how relationship memory, Relationship Pulse, and context-aware writing can help a team stay consistent across LinkedIn and X.'
    }
  },
  ui: {
    headings: {
      composer: 'Email',
      settings: 'Email settings',
      templates: 'Email templates'
    },
    labels: {
      selectedRecipient: 'Selected recipient',
      tabEmail: 'Email',
      leadEmail: 'Email',
      emailHistory: 'Email history',
      expandEmailLog: 'Expand email log',
      collapseEmailLog: 'Collapse email log',
      sentAt: 'Sent at',
      preparedAt: 'Prepared at',
      templateNotRecorded: 'Template not recorded',
      sender: 'Sender',
      replyTo: 'Reply-to',
      cc: 'CC',
      bcc: 'BCC',
      signature: 'Signature',
      includeSignature: 'Include signature',
      to: 'To',
      subject: 'Subject',
      template: 'Email template',
      previewTo: 'To',
      previewFrom: 'From',
      previewTemplate: 'Template',
      previewSubject: 'Subject'
    },
    buttons: {
      saveSettings: 'Save settings',
      useZextriSignature: 'Use Zextri signature',
      insertSignature: 'Insert signature',
      personalize: 'Personalize',
      preview: 'Preview',
      resetToTemplate: 'Reset to template',
      sendTestEmail: 'Send test email',
      sendEmail: 'Send email'
    },
    placeholders: {
      cc: 'manager@yourcompany.com',
      bcc: 'archive@yourcompany.com',
      recipient: 'recipient@company.com',
      subject: 'Subject'
    },
    toolbar: {
      signatureFormatting: 'Signature formatting',
      emailFormatting: 'Email formatting',
      bold: 'Bold',
      italic: 'Italic',
      bulletedList: 'Bulleted list',
      numberedList: 'Numbered list',
      addLink: 'Add link',
      addLinkPrompt: 'Link URL',
      linkLabel: 'Link'
    },
    aria: {
      signatureEditor: 'Email signature editor',
      messageEditor: 'Email message editor',
      resolvedPreview: 'Resolved email preview'
    },
    empty: {
      loadingTemplates: 'Loading templates...',
      noLeadSelected: 'No lead selected',
      noEmail: 'no email',
      noPublicEmail: 'No public email',
      noEmailLog: 'No email history yet.'
    },
    messages: {
      signatureApplied: 'Market-ready Zextri signature applied.',
      emailSettingsSaved: 'Email settings saved for this browser.',
      addSignatureFirst: 'Add a signature in email settings first.',
      savedSettingsLoadFailed: 'Saved email settings could not be loaded.',
      testEmailSentPrefix: 'Test email sent to',
      emailSubmittedPrefix: 'Email submitted to'
    },
    confirms: {
      unsavedDraftSwitch: 'You have an unsaved email draft. Switch contacts and replace the draft?',
      replaceDraft: 'Replace the current draft with this template?'
    },
    toasts: {
      templatesLoadingTitle: 'Templates loading',
      templatesLoadingBody: 'Email templates are still loading. Try again in a moment.',
      templatesUnavailableTitle: 'Templates unavailable',
      noValidTemplatesBody: 'No valid email templates were found.',
      templateAssetsUnavailableBody: 'Email template assets could not be loaded. Restart the UI server so Angular serves the new assets folder.',
      selectedTemplateInvalidBody: 'The selected email template is not valid.',
      selectedTemplateLoadFailedBody: 'The current draft was preserved because the template file could not be loaded.',
      emailNotReadyTitle: 'Email not ready',
      selectLeadBody: 'Select a lead before sending.',
      draftBelongsToAnotherLeadBody: 'The draft belongs to another lead. Refresh the selected contact.',
      validRecipientBody: 'Enter a valid recipient email address.',
      missingLeadEmailBody: 'This lead does not have a saved email address.',
      recipientMismatchBody: 'Recipient email must match the selected lead.',
      subjectAndBodyRequiredBody: 'Subject and message are required.',
      requiredVariablesBody: 'Required template variables are missing or unresolved.',
      emailSentTitle: 'Email sent',
      emailFailedTitle: 'Email failed',
      emailFailedBody: 'Email could not be sent. Check SMTP settings and try again.',
      contactAddedTitle: 'Contact added',
      contactNotAddedTitle: 'Contact not added'
    },
    warnings: {
      noLead: 'No lead is selected.',
      missingGreeting: 'Greeting may be missing because the lead name is blank.',
      missingRecipient: 'Recipient email is missing for this lead.',
      recipientMismatch: 'Recipient email does not match the selected lead.',
      missingRequiredVariablePrefix: 'Required template variable is missing:',
      unresolvedVariablesPrefix: 'Unresolved variables remain:'
    }
  },
  confirmation: {
    sendTestTitle: 'Send test email?',
    sendTitle: 'Send email?',
    lead: 'Lead:',
    to: 'To:',
    from: 'From:',
    template: 'Template:',
    subject: 'Subject:'
  },
  preview: {
    customDraftName: 'Custom draft',
    draftVersion: 'draft'
  },
  legacyDetection: {
    signatureTextMarkers: [
      'best regards',
      'warm regards',
      'shreya singh',
      'zextri growth team',
      'zextri team',
      'website: https://zextri.com',
      'zextri.com'
    ],
    zextriSignatureMarkers: [
      'ai-assisted operations for faster-moving teams',
      'ai-powered social intelligence for linkedin and x',
      'write smarter. follow up better.'
    ]
  }
} as const;

export type EmailTemplateId = 'quick-introduction' | 'relationship-value' | 'demo-follow-up';

export const EmailTemplateStrings: Record<EmailTemplateId, {
  name: string;
  description: string;
  category: string;
  intendedUse: string;
  defaultSubject: string;
}> = {
  'quick-introduction': EmailStrings.templates.quickIntroduction,
  'relationship-value': EmailStrings.templates.relationshipValue,
  'demo-follow-up': EmailStrings.templates.demoFollowUp
};
