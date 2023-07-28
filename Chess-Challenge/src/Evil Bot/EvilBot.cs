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
        /*
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
    }
        */

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
    }
}