import { propertyFeatureEnabled, propertyTokens } from '@trelliskit/shared';
import * as Device from 'expo-device';
import { Platform } from 'react-native';
import { pushPlatform } from '../notifications/push';

/**
 * Support helpers (docs/PHASE5_CONTRACT.md section 6, as-built note C).
 */

/** Ticket title and body limits (contract section 6.1). */
export const TicketLimits = Object.freeze({ titleMin: 3, titleMax: 140, bodyMin: 10, bodyMax: 10000, attachments: 5 });

/** Member ticket statuses in display order. */
export const TicketStatuses = Object.freeze(['Open', 'InProgress', 'WaitingOnMember', 'Resolved', 'Closed']);

/**
 * The device diagnostics the mobile app attaches to a new ticket when the member leaves the
 * toggle on (sent as `X-Client-Platform`, `X-Client-Os-Version`, and `X-Client-Device-Model` on
 * `POST /tickets` only, through the shared `createTicket`).
 * @param {{ os?: string, version?: string|number, model?: string|null }} [overrides]
 * @returns {{ platform: string, osVersion: string, deviceModel: string|null }}
 */
export function deviceDiagnostics({ os = Platform.OS, version = Platform.Version, model = Device.modelName } = {}) {
  return { platform: pushPlatform(os), osVersion: version === undefined || version === null ? '' : String(version), deviceModel: model || null };
}

/** Whether support tickets are on for the property (`features.support`, default on). */
export function supportEnabled(property) {
  return propertyFeatureEnabled(property, 'support');
}

/** The property's support email address, or ''. */
export function supportEmailOf(property) {
  return propertyTokens(property).supportEmail || '';
}

/** Validate the new ticket form; returns `{ field: message }` keys for the strings module. */
export function ticketProblems({ type, title, body }) {
  const problems = {};
  if (!type) problems.type = 'typeRequired';
  if (String(title || '').trim().length < TicketLimits.titleMin) problems.title = 'subjectTooShort';
  if (String(body || '').trim().length < TicketLimits.bodyMin) problems.body = 'bodyTooShort';
  return problems;
}
