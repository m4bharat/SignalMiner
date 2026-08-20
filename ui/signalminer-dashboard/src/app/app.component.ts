import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

type ContactStatus = 'NotContacted' | 'ReadyForManualOutreach' | 'Contacted' | 'Replied' | 'NotInterested' | 'DoNotContact';
type LeadStatus = 'New' | 'Enriched' | 'NeedsManualReview' | 'Qualified' | 'Disqualified' | 'Archived';
type DiscoverySource = 'GitHub' | 'X';
type SourceKind = 'GitHub' | 'Website' | 'X' | 'LinkedInProfileUrlOnly' | 'Manual';
type SourceFilter = '' | 'Imported' | SourceKind;

interface Lead {
  id: string;
  displayName: string;
  roleTitle?: string;
  publicEmail?: string;
  websiteUrl?: string;
  gitHubUrl?: string;
  xUrl?: string;
  linkedInUrl?: string;
  fitScore: number;
  scoreRationale: string;
  status: LeadStatus;
  contactStatus: ContactStatus;
  notes?: string;
  company?: { name: string; domain?: string; summary?: string };
  sourceProfiles?: SourceProfile[];
}

interface SourceProfile {
  kind: string;
  url: string;
  publicHandle: string;
}

interface SearchResult {
  items: Lead[];
  total: number;
}

interface LeadImportResult {
  totalRows: number;
  validRows: number;
  importedRows: number;
  skippedRows: number;
  issues: LeadImportIssue[];
  previewRows: LeadImportPreviewRow[];
}

interface LeadImportIssue {
  rowNumber: number;
  field: string;
  message: string;
}

interface LeadImportPreviewRow {
  rowNumber: number;
  displayName: string;
  company?: string;
  publicEmail?: string;
  fitScore: number;
  isDuplicate: boolean;
  duplicateReason?: string;
}

@Component({
  selector: 'sm-root',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './app.component.html'
})
export class AppComponent {
  private readonly http = inject(HttpClient);
  private readonly apiBase = 'http://localhost:5126/api';

  protected readonly leads = signal<Lead[]>([]);
  protected readonly selectedLead = signal<Lead | null>(null);
  protected readonly total = signal(0);
  protected readonly query = signal('');
  protected readonly minFitScore = signal<number | null>(50);
  protected readonly contactStatus = signal<ContactStatus | ''>('');
  protected readonly sourceFilter = signal<SourceFilter>('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly discoveryQuery = signal('founder saas ai');
  protected readonly discoverySource = signal<DiscoverySource>('GitHub');
  protected readonly loading = signal(false);
  protected readonly message = signal('Manual-review-first lead discovery workspace');
  protected readonly outreachNote = signal('');
  protected readonly emailSubject = signal('');
  protected readonly emailBodyHtml = signal('');
  protected readonly emailAttachments = signal<File[]>([]);
  protected readonly importFile = signal<File | null>(null);
  protected readonly importResult = signal<LeadImportResult | null>(null);

  protected readonly templates = computed(() => {
    const lead = this.selectedLead();
    const name = lead?.displayName.split(' ')[0] ?? 'there';
    return [
      `Hi ${name}, I noticed your work around AI/SaaS and thought Zextri might be relevant. Worth a quick look?`,
      `Hi ${name}, your public GitHub/website signals suggest you may be building workflow software. Zextri helps teams move faster with AI-assisted operations.`,
      `Hi ${name}, I am doing a manual review of potential Zextri fits and your profile stood out. Open to a short note with details?`
    ];
  });

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  protected readonly pageStart = computed(() => {
    if (this.total() === 0) return 0;
    return (this.page() - 1) * this.pageSize() + 1;
  });

  protected readonly pageEnd = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  constructor() {
    this.search();
  }

  protected search(resetPage = true): void {
    if (resetPage) {
      this.page.set(1);
    }

    this.loading.set(true);
    let params = new HttpParams()
      .set('page', this.page())
      .set('pageSize', this.pageSize());

    if (this.query()) params = params.set('query', this.query());
    if (this.minFitScore() !== null) params = params.set('minFitScore', this.minFitScore()!.toString());
    if (this.contactStatus()) params = params.set('contactStatus', this.contactStatus());
    if (this.sourceFilter() === 'Imported') {
      params = params.set('importedOnly', 'true');
    } else if (this.sourceFilter()) {
      params = params.set('sourceKind', this.sourceFilter());
    }

    this.http.get<SearchResult>(`${this.apiBase}/leads/search`, { params }).subscribe({
      next: result => {
        this.leads.set(result.items);
        this.total.set(result.total);
        this.selectedLead.set(result.items[0] ?? null);
        if (this.page() > this.totalPages()) {
          this.page.set(this.totalPages());
          this.search(false);
          return;
        }
        this.loading.set(false);
      },
      error: () => {
        this.message.set('API unavailable. Start SignalMiner.Api on http://localhost:5000.');
        this.loading.set(false);
      }
    });
  }

  protected discover(): void {
    this.loading.set(true);
    this.http.post<Lead[]>(`${this.apiBase}/leads/discover`, {
      query: this.discoveryQuery(),
      limit: 25,
      source: this.discoverySource()
    }).subscribe({
      next: leads => {
        this.message.set(`Discovered ${leads.length} ${this.discoverySource()} leads for manual review.`);
        this.search();
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Discovery failed. Check API, PostgreSQL, and source rate limits.');
        this.loading.set(false);
      }
    });
  }

  protected selectImportFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.importFile.set(file);
    this.importResult.set(null);
    if (file) {
      this.message.set(`Ready to preview ${file.name}.`);
    }
  }

  protected previewImport(): void {
    this.uploadImport(false);
  }

  protected commitImport(): void {
    this.uploadImport(true);
  }

  protected primarySource(lead: Lead): { label: string; url?: string } {
    if (lead.gitHubUrl) return { label: 'GitHub', url: lead.gitHubUrl };
    if (lead.xUrl) return { label: 'X', url: lead.xUrl };
    if (lead.linkedInUrl) return { label: 'LinkedIn', url: lead.linkedInUrl };
    if (lead.websiteUrl) return { label: 'Website', url: lead.websiteUrl };
    return { label: 'None' };
  }

  protected clearFilters(): void {
    this.query.set('');
    this.minFitScore.set(null);
    this.contactStatus.set('');
    this.sourceFilter.set('');
    this.search();
  }

  protected nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update(value => value + 1);
    this.search(false);
  }

  protected previousPage(): void {
    if (this.page() <= 1) return;
    this.page.update(value => value - 1);
    this.search(false);
  }

  protected changePageSize(value: number): void {
    this.pageSize.set(value);
    this.search();
  }

  protected enrich(lead: Lead): void {
    this.loading.set(true);
    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/enrich`, {}).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        this.message.set('Lead enriched from public website data.');
        this.search();
      },
      error: () => {
        this.message.set('Enrichment failed or no crawlable public website was available.');
        this.loading.set(false);
      }
    });
  }

  protected selectLead(lead: Lead): void {
    this.selectedLead.set(lead);
    this.outreachNote.set('');
    this.emailSubject.set(`Quick note from Zextri`);
    this.emailBodyHtml.set(this.textToHtml(this.templates()[0] ?? ''));
    this.emailAttachments.set([]);
  }

  protected useTemplate(template: string): void {
    this.emailBodyHtml.set(this.textToHtml(template));
  }

  protected updateEmailBody(event: Event): void {
    this.emailBodyHtml.set((event.target as HTMLElement).innerHTML);
  }

  protected formatEmail(command: string, value?: string): void {
    document.execCommand(command, false, value);
  }

  protected addEmailLink(): void {
    const url = window.prompt('Link URL');
    if (!url) return;

    const normalizedUrl = url.startsWith('http') ? url : `https://${url}`;
    this.formatEmail('createLink', normalizedUrl);
  }

  protected selectEmailAttachments(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.emailAttachments.set(Array.from(input.files ?? []));
  }

  protected sendEmail(): void {
    const lead = this.selectedLead();
    if (!lead) return;

    if (!lead.publicEmail) {
      this.message.set('This lead does not have an email address.');
      return;
    }

    this.loading.set(true);
    const form = new FormData();
    form.append('subject', this.emailSubject());
    form.append('body', this.htmlToText(this.emailBodyHtml()));
    for (const file of this.emailAttachments()) {
      form.append('attachments', file, file.name);
    }

    this.http.post<Lead>(`${this.apiBase}/leads/${lead.id}/email`, form).subscribe({
      next: updated => {
        this.selectedLead.set(updated);
        this.message.set(`Email sent to ${updated.publicEmail}.`);
        this.loading.set(false);
        this.search(false);
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Email could not be sent. Check SMTP settings and try again.');
        this.loading.set(false);
      }
    });
  }

  private textToHtml(value: string): string {
    return value
      .split(/\r?\n/)
      .map(line => line.trim())
      .filter(line => line.length > 0)
      .map(line => `<p>${this.escapeHtml(line)}</p>`)
      .join('');
  }

  private htmlToText(value: string): string {
    const container = document.createElement('div');
    container.innerHTML = value;
    return (container.innerText || container.textContent || '').trim();
  }

  private escapeHtml(value: string): string {
    const container = document.createElement('div');
    container.textContent = value;
    return container.innerHTML;
  }

  protected updateStatus(status: ContactStatus): void {
    const lead = this.selectedLead();
    if (!lead) return;

    this.http.patch<Lead>(`${this.apiBase}/leads/${lead.id}/outreach-status`, {
      status,
      note: this.outreachNote() || `Manual status changed to ${status}`
    }).subscribe(updated => {
      this.selectedLead.set(updated);
      this.message.set('Manual outreach status updated.');
      this.search();
    });
  }

  private uploadImport(commit: boolean): void {
    const file = this.importFile();
    if (!file) {
      this.message.set('Choose a CSV or Excel file first.');
      return;
    }

    this.loading.set(true);
    const form = new FormData();
    form.append('file', file);
    form.append('commit', String(commit));

    this.http.post<LeadImportResult>(`${this.apiBase}/leads/import`, form).subscribe({
      next: result => {
        this.importResult.set(result);
        this.message.set(commit
          ? `Imported ${result.importedRows} leads; skipped ${result.skippedRows}.`
          : `Previewed ${result.validRows} valid rows from ${result.totalRows}.`);
        this.loading.set(false);
        if (commit) {
          this.sourceFilter.set('Imported');
          this.search();
        }
      },
      error: (error: HttpErrorResponse) => {
        this.message.set(error.error?.message || 'Import failed. Check the file format and required columns.');
        this.loading.set(false);
      }
    });
  }
}
