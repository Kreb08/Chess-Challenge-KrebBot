//#define EVIL
//#define TIER1
#define TIER2

using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
#if EVIL
        // Piece values: null, pawn, knight, bishop, rook, queen, king
        int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

        public Move Think(Board board, Timer timer)
        {
            Move[] allMoves = board.GetLegalMoves();

            // Pick a random move to play if nothing better is found
            Random rng = new();
            Move moveToPlay = allMoves[rng.Next(allMoves.Length)];
            int highestValueCapture = 0;

            foreach (Move move in allMoves)
            {
                // Always play checkmate in one
                if (MoveIsCheckmate(board, move))
                {
                    moveToPlay = move;
                    break;
                }

                // Find highest value capture
                Piece capturedPiece = board.GetPiece(move.TargetSquare);
                int capturedPieceValue = pieceValues[(int)capturedPiece.PieceType];

                if (capturedPieceValue > highestValueCapture)
                {
                    moveToPlay = move;
                    highestValueCapture = capturedPieceValue;
                }
            }

            return moveToPlay;
        }

        // Test if this move gives checkmate
        bool MoveIsCheckmate(Board board, Move move)
        {
            board.MakeMove(move);
            bool isMate = board.IsInCheckmate();
            board.UndoMove(move);
            return isMate;
        }
#endif
#if TIER1

        //                     .  P    K    B    R    Q    K
        int[] kPieceValues = { 0, 100, 300, 310, 500, 900, 10000 };
        int kMassiveNum = 99999999;

        int mDepth;
        Move mBestMove;

        public Move Think(Board board, Timer timer)
        {
            Move[] legalMoves = board.GetLegalMoves();
            mDepth = 3;

            EvaluateBoardNegaMax(board, mDepth, -kMassiveNum, kMassiveNum, board.IsWhiteToMove ? 1 : -1);

            return mBestMove;
        }

        int EvaluateBoardNegaMax(Board board, int depth, int alpha, int beta, int color)
        {
            Move[] legalMoves;

            if (board.IsDraw())
                return 0;

            if (depth == 0 || (legalMoves = board.GetLegalMoves()).Length == 0)
            {
                // EVALUATE
                int sum = 0;

                if (board.IsInCheckmate())
                    return -9999999;

                for (int i = 0; ++i < 7;)
                    sum += (board.GetPieceList((PieceType)i, true).Count - board.GetPieceList((PieceType)i, false).Count) * kPieceValues[i];
                // EVALUATE

                return color * sum;
            }

            // TREE SEARCH
            int recordEval = int.MinValue;
            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);
                int evaluation = -EvaluateBoardNegaMax(board, depth - 1, -beta, -alpha, -color);
                board.UndoMove(move);

                if (recordEval < evaluation)
                {
                    recordEval = evaluation;
                    if (depth == mDepth)
                        mBestMove = move;
                }
                alpha = Math.Max(alpha, recordEval);
                if (alpha >= beta) break;
            }
            // TREE SEARCH

            return recordEval;
        }
#endif
#if TIER2
        Move bestmoveRoot = Move.NullMove;

        // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
        int[] pieceVal = { 0, 100, 310, 330, 500, 1000, 10000 };
        int[] piecePhase = { 0, 0, 1, 1, 2, 4, 0 };
        ulong[] psts = { 657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902 };

        // https://www.chessprogramming.org/Transposition_Table
        struct TTEntry
        {
            public ulong key;
            public Move move;
            public int depth, score, bound;
            public TTEntry(ulong _key, Move _move, int _depth, int _score, int _bound)
            {
                key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
            }
        }

        const int entries = (1 << 20);
        TTEntry[] tt = new TTEntry[entries];

        public int getPstVal(int psq)
        {
            return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
        }

        public int Evaluate(Board board)
        {
            int mg = 0, eg = 0, phase = 0;

            foreach (bool stm in new[] { true, false })
            {
                for (var p = PieceType.Pawn; p <= PieceType.King; p++)
                {
                    int piece = (int)p, ind;
                    ulong mask = board.GetPieceBitboard(p, stm);
                    while (mask != 0)
                    {
                        phase += piecePhase[piece];
                        ind = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (stm ? 56 : 0);
                        mg += getPstVal(ind) + pieceVal[piece];
                        eg += getPstVal(ind + 64) + pieceVal[piece];
                    }
                }

                mg = -mg;
                eg = -eg;
            }

            return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
        }

        // https://www.chessprogramming.org/Negamax
        // https://www.chessprogramming.org/Quiescence_Search
        public int Search(Board board, Timer timer, int alpha, int beta, int depth, int ply)
        {
            ulong key = board.ZobristKey;
            bool qsearch = depth <= 0;
            bool notRoot = ply > 0;
            int best = -30000;

            // Check for repetition (this is much more important than material and 50 move rule draws)
            if (notRoot && board.IsRepeatedPosition())
                return 0;

            TTEntry entry = tt[key % entries];

            // TT cutoffs
            if (notRoot && entry.key == key && entry.depth >= depth && (
                entry.bound == 3 // exact score
                    || entry.bound == 2 && entry.score >= beta // lower bound, fail high
                    || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
            )) return entry.score;

            int eval = Evaluate(board);

            // Quiescence search is in the same function as negamax to save tokens
            if (qsearch)
            {
                best = eval;
                if (best >= beta) return best;
                alpha = Math.Max(alpha, best);
            }

            // Generate moves, only captures in qsearch
            Move[] moves = board.GetLegalMoves(qsearch);
            int[] scores = new int[moves.Length];

            // Score moves
            for (int i = 0; i < moves.Length; i++)
            {
                Move move = moves[i];
                // TT move
                if (move == entry.move) scores[i] = 1000000;
                // https://www.chessprogramming.org/MVV-LVA
                else if (move.IsCapture) scores[i] = 100 * (int)move.CapturePieceType - (int)move.MovePieceType;
            }

            Move bestMove = Move.NullMove;
            int origAlpha = alpha;

            // Search moves
            for (int i = 0; i < moves.Length; i++)
            {
                if (timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30) return 30000;

                // Incrementally sort moves
                for (int j = i + 1; j < moves.Length; j++)
                {
                    if (scores[j] > scores[i])
                        (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
                }

                Move move = moves[i];
                board.MakeMove(move);
                int score = -Search(board, timer, -beta, -alpha, depth - 1, ply + 1);
                board.UndoMove(move);

                // New best move
                if (score > best)
                {
                    best = score;
                    bestMove = move;
                    if (ply == 0) bestmoveRoot = move;

                    // Improve alpha
                    alpha = Math.Max(alpha, score);

                    // Fail-high
                    if (alpha >= beta) break;

                }
            }

            // (Check/Stale)mate
            if (!qsearch && moves.Length == 0) return board.IsInCheck() ? -30000 + ply : 0;

            // Did we fail high/low or get an exact score?
            int bound = best >= beta ? 2 : best > origAlpha ? 3 : 1;

            // Push to TT
            tt[key % entries] = new TTEntry(key, bestMove, depth, best, bound);

            return best;
        }

        public Move Think(Board board, Timer timer)
        {
            bestmoveRoot = Move.NullMove;
            // https://www.chessprogramming.org/Iterative_Deepening
            for (int depth = 1; depth <= 50; depth++)
            {
                int score = Search(board, timer, -30000, 30000, depth, 0);

                // Out of time
                if (timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30)
                    break;
            }
            return bestmoveRoot.IsNull ? board.GetLegalMoves()[0] : bestmoveRoot;
        }
#endif
    }
}