// Backstories: who each survivor was before. Generated from a hash of their id
// (never the sim RNG), so making someone doesn't change anything else. Stored as
// small numbers and names; the text is put together when it's told (talk.ts).

import { hashRoll, idNumber } from './hash';
import type { TaskKind } from './talk';

export interface Story {
  /** Index into STORY_JOBS. */
  job: number;
  /** Index into PRIDES: the thing they did right. */
  pride: number;
  /** Index into REGRETS: the thing they can't put down, and the place it happened. */
  regret: number;
  /** Someone they lost. Dead ones lie unburied somewhere. */
  lost: { name: string; relation: string; dead: boolean };
  /** A dog they had to leave behind, by name, or null. */
  pet: string | null;
  /** How many of the story's lines they've told you (talk.ts). */
  told: number;
  /** Not ready to talk again before this tick. */
  nextTalk: number;
  /** Tasks they've asked for so far (talk.ts). */
  asked: TaskKind[];
}

export const STORY_JOBS = [
  'a nurse', 'a mechanic', 'a teacher', 'a farmer', 'a soldier', 'a cook', 'a police officer',
  'a carpenter', 'a paramedic', 'a lorry driver', 'an electrician', 'a priest', 'a shop clerk', 'a student',
];
export const PRIDES = [
  'Kept a ward running for three days after the power went',
  'Got half the street out before the bridge came down',
  'Never once left anyone behind on a supply run',
  'Talked a man down from a roof in the first week',
  'Held a church door shut all night so the children could get out the back',
  'Delivered a baby in a stairwell, with the dead banging on the door',
  'Walked forty miles with a broken wrist and didn\'t stop',
];
export const REGRETS = [
  'I locked the shelter door with the neighbours still outside',
  'I ran when the clinic was overrun, and the patients didn\'t',
  'I took a family\'s food the second winter. They had kids',
  'I didn\'t go back for my father. I heard him calling',
  'I shot a man at a petrol station who only wanted water',
  'I left the school unlocked. They got in that night',
];
/** Each relation with names that fit it. */
const RELATIONS: readonly (readonly [string, readonly string[]])[] = [
  ['wife', ['Anna', 'Marta', 'Lena', 'Clara']],
  ['husband', ['Tom', 'Josef', 'Owen', 'Hugh']],
  ['sister', ['Iris', 'Nadia', 'Clara', 'Ruth']],
  ['brother', ['Pete', 'Ben', 'Owen', 'Karl']],
  ['son', ['Ben', 'Leo', 'Sam', 'Danny']],
  ['daughter', ['Lily', 'Ella', 'Rosie', 'Mia']],
  ['mother', ['Margaret', 'Helen', 'Agnes', 'Vera']],
  ['father', ['Frank', 'George', 'Walter', 'Stan']],
  ['best friend', ['Robin', 'Alex', 'Jo', 'Charlie']],
];
const PET_NAMES = ['Bruno', 'Daisy', 'Pepper', 'Max', 'Luna', 'Rocky', 'Bella', 'Duke'];

export function makeStory(id: string): Story {
  const n = idNumber(id);
  const pick = <T>(list: readonly T[], salt: number) => list[Math.floor(hashRoll(n, salt) * list.length)]!;
  return {
    job: Math.floor(hashRoll(n, 0x51) * STORY_JOBS.length),
    pride: Math.floor(hashRoll(n, 0x52) * PRIDES.length),
    regret: Math.floor(hashRoll(n, 0x53) * REGRETS.length),
    lost: lostOne(n),
    pet: hashRoll(n, 0x57) < 0.5 ? pick(PET_NAMES, 0x58) : null,
    told: 0,
    nextTalk: 0,
    asked: [],
  };
}

function lostOne(n: number): Story['lost'] {
  const [relation, names] = RELATIONS[Math.floor(hashRoll(n, 0x55) * RELATIONS.length)]!;
  return { name: names[Math.floor(hashRoll(n, 0x54) * names.length)]!, relation, dead: hashRoll(n, 0x56) < 0.6 };
}

/** The lines of a story, in the order they're told. */
export function storyLines(story: Story): string[] {
  const { lost } = story;
  return [
    `I was ${STORY_JOBS[story.job]}. ${PRIDES[story.pride]}.`,
    lost.dead
      ? `My ${lost.relation}, ${lost.name}. They didn't make it, and I left them where they fell. They deserve a grave.`
      : `My ${lost.relation}, ${lost.name}. I don't know where they are. I tell myself that's better.`,
    story.pet ? `I had a dog, ${story.pet}. I left ${story.pet} shut in at home. I think about that a lot.` : 'I still think about home. What\'s left of it.',
    `${REGRETS[story.regret]}. I can't stop seeing that place.`,
  ];
}
